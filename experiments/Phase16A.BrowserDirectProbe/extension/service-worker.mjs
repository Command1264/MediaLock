import {
  createReplayGuard,
  validateHelloAck,
  validateHostHello,
  validateNativeCommand,
} from './protocol.mjs';

const NATIVE_HOST_NAME = 'com.command1264.medialock.phase16a';
const ALLOWED_PAGE_ORIGINS = new Set([
  'https://www.youtube.com',
  'https://music.youtube.com',
]);
const COMMAND_TIMEOUT_MILLISECONDS = 5000;

let nativePort;
let activeSessionId;
let replayGuard;
let negotiated = false;
let outboundSequence = 0;
const pendingRequests = new Map();

connectNativeHost();

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id
      || sender.url !== chrome.runtime.getURL('popup.html')) {
    sendResponse({ accepted: false, errorCode: 'unauthorized-command' });
    return false;
  }

  queueProbeRequest(message).then(sendResponse, () => {
    sendResponse({ accepted: false, errorCode: 'request-rejected' });
  });
  return true;
});

function connectNativeHost() {
  nativePort = chrome.runtime.connectNative(NATIVE_HOST_NAME);
  resetConnectionState('native-host-disconnected');
  nativePort.onMessage.addListener(handleNativeMessage);
  nativePort.onDisconnect.addListener(() => {
    resetConnectionState('native-host-disconnected');
    nativePort = undefined;
  });
}

function resetConnectionState(errorCode) {
  activeSessionId = undefined;
  replayGuard = undefined;
  negotiated = false;
  outboundSequence = 0;
  for (const pending of pendingRequests.values()) {
    clearTimeout(pending.timeoutId);
    pending.resolve({ accepted: false, errorCode });
  }
  pendingRequests.clear();
}

async function queueProbeRequest(message) {
  const command = validatePopupCommand(message);
  if (!negotiated || !nativePort || !activeSessionId) {
    return { accepted: false, errorCode: 'native-host-unavailable' };
  }

  const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
  if (tabs.length !== 1 || !Number.isSafeInteger(tabs[0].id) || typeof tabs[0].url !== 'string') {
    return { accepted: false, errorCode: 'target-unavailable' };
  }
  const pageOrigin = new URL(tabs[0].url).origin;
  if (!ALLOWED_PAGE_ORIGINS.has(pageOrigin)) {
    return { accepted: false, errorCode: 'target-unavailable' };
  }

  const requestId = crypto.randomUUID();
  const result = new Promise((resolve) => {
    const timeoutId = setTimeout(() => {
      pendingRequests.delete(requestId);
      resolve({ accepted: false, errorCode: 'outcome-unknown' });
    }, COMMAND_TIMEOUT_MILLISECONDS);
    pendingRequests.set(requestId, { resolve, timeoutId });
  });

  outboundSequence += 1;
  nativePort.postMessage({
    protocolVersion: 1,
    type: 'probeRequest',
    sessionId: activeSessionId,
    sequence: outboundSequence,
    requestId,
    target: {
      tabId: tabs[0].id,
      frameId: 0,
      pageOrigin,
    },
    command,
  });
  return result;
}

async function handleNativeMessage(message) {
  try {
    if (!activeSessionId) {
      const hello = validateHostHello(message);
      activeSessionId = hello.sessionId;
      replayGuard = createReplayGuard(1024);
      nativePort.postMessage({
        protocolVersion: 1,
        type: 'extensionHello',
        sessionId: activeSessionId,
        extensionId: chrome.runtime.id,
      });
      return;
    }

    if (!negotiated) {
      validateHelloAck(message, activeSessionId);
      negotiated = true;
      return;
    }

    const request = validateNativeCommand(message, activeSessionId, replayGuard);
    const pending = pendingRequests.get(request.requestId);
    if (!pending) {
      throw new Error('Native Host returned a stale or unknown request.');
    }

    const tab = await chrome.tabs.get(request.target.tabId);
    const actualOrigin = new URL(tab.url).origin;
    if (actualOrigin !== request.target.pageOrigin) {
      completeRequest(request, pending, false, 'target-unavailable');
      return;
    }

    let result;
    try {
      result = await chrome.tabs.sendMessage(
        request.target.tabId,
        request,
        { frameId: 0 },
      );
    } catch {
      result = { accepted: false, errorCode: 'target-unavailable' };
    }
    completeRequest(
      request,
      pending,
      result?.accepted === true,
      result?.accepted === true ? null : normalizeResultError(result?.errorCode),
    );
  } catch {
    nativePort?.disconnect();
    resetConnectionState('protocol-rejected');
  }
}

function completeRequest(request, pending, accepted, errorCode) {
  pendingRequests.delete(request.requestId);
  clearTimeout(pending.timeoutId);
  outboundSequence += 1;
  nativePort.postMessage({
    protocolVersion: 1,
    type: 'commandResult',
    sessionId: activeSessionId,
    sequence: outboundSequence,
    requestId: request.requestId,
    accepted,
    errorCode,
  });
  pending.resolve({ accepted, errorCode });
}

function validatePopupCommand(message) {
  if (message === null || typeof message !== 'object' || Array.isArray(message)
      || Object.keys(message).length !== 2
      || message.type !== 'probeCommand'
      || message.command === null || typeof message.command !== 'object') {
    throw new TypeError('Popup command is invalid.');
  }
  const commandFields = Object.keys(message.command).sort();
  const name = message.command.name;
  if (name === 'play' || name === 'pause') {
    if (commandFields.length !== 1 || commandFields[0] !== 'name') {
      throw new TypeError('Popup command fields are invalid.');
    }
    return { name };
  }
  if (name === 'seek') {
    if (commandFields.length !== 2
        || commandFields[0] !== 'name'
        || commandFields[1] !== 'positionSeconds'
        || !Number.isFinite(message.command.positionSeconds)
        || message.command.positionSeconds < 0) {
      throw new TypeError('Popup seek command is invalid.');
    }
    return { name, positionSeconds: message.command.positionSeconds };
  }
  throw new TypeError('Popup command is not allowed.');
}

function normalizeResultError(errorCode) {
  const allowed = new Set([
    'media-element-unavailable',
    'seek-out-of-range',
    'play-rejected',
    'unauthorized-command',
    'target-unavailable',
  ]);
  return allowed.has(errorCode) ? errorCode : 'target-unavailable';
}
