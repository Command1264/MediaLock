import {
  createNativeHostReadinessGate,
  createReplayGuard,
  validateHelloAck,
  validateHostHello,
  validateNativeCommand,
} from './protocol.mjs';
import { createDocumentRegistry } from './document-binding.mjs';

const NATIVE_HOST_NAME = 'com.command1264.medialock.phase16a';
const ALLOWED_PAGE_ORIGINS = new Set([
  'https://www.youtube.com',
  'https://music.youtube.com',
]);
const COMMAND_TIMEOUT_MILLISECONDS = 5000;
const INITIAL_NEGOTIATION_TIMEOUT_MILLISECONDS = 5000;
const MAXIMUM_PENDING_REQUESTS = 64;

let nativePort;
let activeConnection;
let replayGuard;
let negotiated = false;
let outboundSequence = 0;
const pendingRequests = new Map();
const documentRegistry = createDocumentRegistry(chrome.runtime.id);
const nativeHostReadiness = createNativeHostReadinessGate(
  INITIAL_NEGOTIATION_TIMEOUT_MILLISECONDS,
);

connectNativeHost();

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === 'registerDocument') {
    try {
      const binding = documentRegistry.register(sender);
      sendResponse({ accepted: true, documentId: binding.documentId });
    } catch {
      sendResponse({ accepted: false, errorCode: 'unauthorized-command' });
    }
    return false;
  }

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
  nativeHostReadiness.reset();
  activeConnection = undefined;
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
  if (!nativePort) {
    return { accepted: false, errorCode: 'native-host-unavailable' };
  }
  if (!negotiated && !await nativeHostReadiness.waitUntilReady()) {
    return { accepted: false, errorCode: 'native-host-unavailable' };
  }
  if (!negotiated || !nativePort || !activeConnection) {
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
  let registration;
  try {
    registration = await chrome.tabs.sendMessage(
      tabs[0].id,
      { type: 'requestDocumentRegistration' },
      { frameId: 0 },
    );
  } catch {
    return { accepted: false, errorCode: 'target-unavailable' };
  }
  const target = documentRegistry.get(tabs[0].id);
  if (registration?.accepted !== true
      || !target
      || target.documentId !== registration.documentId
      || target.pageOrigin !== pageOrigin) {
    return { accepted: false, errorCode: 'target-unavailable' };
  }

  const requestId = crypto.randomUUID();
  if (pendingRequests.size >= MAXIMUM_PENDING_REQUESTS) {
    return { accepted: false, errorCode: 'outcome-unknown' };
  }
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
    connectionId: activeConnection.connectionId,
    sequence: outboundSequence,
    requestId,
    target,
    command,
  });
  return result;
}

async function handleNativeMessage(message) {
  try {
    if (!activeConnection) {
      const hello = validateHostHello(message);
      const extensionNonce = crypto.randomUUID();
      const browserFamily = await detectBrowserFamily();
      const capabilities = hello.capabilities.filter(
        (capability) => capability === 'pause' || capability === 'play' || capability === 'seek',
      );
      if (capabilities.length === 0) {
        throw new Error('Native Host and Extension have no shared capabilities.');
      }
      activeConnection = {
        extensionId: chrome.runtime.id,
        hostNonce: hello.hostNonce,
        extensionNonce,
        browserFamily,
        capabilities,
      };
      replayGuard = createReplayGuard(1024);
      nativePort.postMessage({
        protocolVersion: 1,
        type: 'extensionHello',
        hostNonce: hello.hostNonce,
        extensionNonce,
        extensionId: chrome.runtime.id,
        browserFamily,
        capabilities,
      });
      return;
    }

    if (!negotiated) {
      const acknowledgement = await validateHelloAck(message, activeConnection);
      activeConnection = { ...activeConnection, connectionId: acknowledgement.connectionId };
      negotiated = true;
      nativeHostReadiness.markReady();
      return;
    }

    const request = validateNativeCommand(
      message,
      activeConnection.connectionId,
      replayGuard,
      activeConnection.capabilities,
    );
    const pending = pendingRequests.get(request.requestId);
    if (!pending) {
      throw new Error('Native Host returned a stale or unknown request.');
    }

    if (!documentRegistry.matches(request.target)) {
      completeRequest(request, pending, false, 'target-unavailable');
      return;
    }

    let result;
    try {
      result = await chrome.tabs.sendMessage(
        request.target.tabId,
        request,
        { documentId: request.target.documentId },
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

chrome.tabs.onRemoved.addListener((tabId) => documentRegistry.clear(tabId));
chrome.tabs.onReplaced.addListener((addedTabId, removedTabId) => {
  documentRegistry.clear(removedTabId);
  documentRegistry.clear(addedTabId);
});
chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.status === 'loading') {
    documentRegistry.clear(tabId);
  }
});

function completeRequest(request, pending, accepted, errorCode) {
  pendingRequests.delete(request.requestId);
  clearTimeout(pending.timeoutId);
  outboundSequence += 1;
  nativePort.postMessage({
    protocolVersion: 1,
    type: 'commandResult',
    connectionId: activeConnection.connectionId,
    sequence: outboundSequence,
    requestId: request.requestId,
    accepted,
    errorCode,
  });
  pending.resolve({ accepted, errorCode });
}

async function detectBrowserFamily() {
  if (typeof navigator.brave?.isBrave === 'function' && await navigator.brave.isBrave()) {
    return 'brave';
  }
  return 'chrome';
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
