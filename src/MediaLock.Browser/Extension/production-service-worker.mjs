import { createBrowserAuthorizationModule } from './browser-authorization.mjs';
import { createAuthorizedPageBindingCoordinator } from './authorized-page-binding.mjs';
import { createAuthorizedTargetLifecycle } from './authorized-target-lifecycle.mjs';
import { dispatchBoundCommand } from './browser-dispatch.mjs';
import { createBrowserMediaTargetRegistry } from './generic-target-registry.mjs';
import { handleNativePortDisconnect } from './native-connection-lifecycle.mjs';
import {
  createInboundCommandGuard,
  validateHelloAck,
  validateHostHello,
} from './production-protocol.mjs';

const NATIVE_HOST_NAME = 'com.command1264.medialock.browser';
const CAPABILITIES = Object.freeze(['pause', 'play', 'seek', 'toggle']);
const HANDSHAKE_TIMEOUT_MILLISECONDS = 5000;
const browserAuthorization = createBrowserAuthorizationModule({
  tabs: chrome.tabs,
  scripting: chrome.scripting,
  permissions: chrome.permissions,
});
const genericTargetRegistry = createBrowserMediaTargetRegistry({
  authorization: browserAuthorization,
  tabs: chrome.tabs,
});
const authorizedTargetLifecycle = createAuthorizedTargetLifecycle({
  publishTarget,
  publishTargetRemoved,
  clearTab: (tabId) => genericTargetRegistry.clearTab(tabId),
});
const pageBindingCoordinator = createAuthorizedPageBindingCoordinator({
  hasTarget: (tabId) => authorizedTargetLifecycle.get(tabId) !== undefined,
  hasSitePermission: (origin) => chrome.permissions.contains({ origins: [`${origin}/*`] }),
  bindTab: (tab, scope) => prepareBinding(
    () => genericTargetRegistry.bindTab({ scope, tab }),
  ),
  commitBinding: (result) => authorizedTargetLifecycle.replace(result),
  discardBinding: (target) => genericTargetRegistry.discard(target),
});

let nativePort;
let handshake;
let connectionId;
let negotiatedCapabilities = [];
let outboundSequence = 0;
let inboundCommandGuard;

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender?.id !== chrome.runtime.id) {
    return false;
  }
  if (message?.type === 'authorizeGenericTarget') {
    authorizeTarget(message.scope).then(sendResponse, () => {
      sendResponse({ accepted: false, errorCode: 'native-host-unavailable' });
    });
    return true;
  }
  if (message?.type === 'genericPresentationChanged') {
    handlePresentationChanged(message, sender).then(
      (accepted) => sendResponse({ accepted }),
      () => sendResponse({ accepted: false }),
    );
    return true;
  }
  return false;
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  const removed = authorizedTargetLifecycle.handleTabUpdated(tabId, changeInfo);
  if (changeInfo?.status === 'loading' && removed !== true) {
    genericTargetRegistry.clearTab(tabId);
  }
  pageBindingCoordinator.handleTabUpdated(tabId, changeInfo, tab).catch((error) => {
    console.debug(
      'Media Lock trusted-site auto-binding did not complete.',
      error instanceof Error ? error.name : 'UnknownError',
    );
  });
});

chrome.tabs.onRemoved.addListener((tabId) => {
  pageBindingCoordinator.invalidate(tabId);
  if (removeTarget(tabId, 'tab-closed') !== true) {
    genericTargetRegistry.clearTab(tabId);
  }
});

chrome.permissions.onRemoved.addListener((removed) => {
  const removedOrigins = new Set(Array.isArray(removed?.origins) ? removed.origins : []);
  for (const entry of [...authorizedTargetLifecycle.values()]) {
    if (entry.target.scope === 'site'
        && removedOrigins.has(`${entry.target.pageOrigin}/*`)) {
      revokeTarget(entry, 'permission-revoked').catch(() => {
        removeTarget(entry.target.tabId, 'permission-revoked');
      });
    }
  }
});

async function authorizeTarget(scope) {
  if (scope !== 'temporary' && scope !== 'site') {
    return { accepted: false, errorCode: 'unauthorized-command' };
  }
  const activeTabs = await chrome.tabs.query({ active: true, currentWindow: true });
  if (activeTabs.length !== 1 || !Number.isSafeInteger(activeTabs[0]?.id)) {
    return { accepted: false, errorCode: 'target-unavailable' };
  }
  const [activeTab] = activeTabs;
  return pageBindingCoordinator.authorizeTab({ scope, tab: activeTab });
}

async function prepareBinding(bind) {
  const result = await bind();
  if (result.accepted !== true) {
    return result;
  }
  if (await ensureNativeConnection() !== true) {
    genericTargetRegistry.discard(result.target);
    return { accepted: false, errorCode: 'native-host-unavailable' };
  }
  return result;
}

async function ensureNativeConnection() {
  if (connectionId) {
    return true;
  }
  if (handshake) {
    return handshake.promise;
  }

  let resolveHandshake;
  const promise = new Promise((resolve) => {
    resolveHandshake = resolve;
  });
  handshake = { promise, resolve: resolveHandshake };
  try {
    nativePort = chrome.runtime.connectNative(NATIVE_HOST_NAME);
    nativePort.onMessage.addListener((message) => {
      handleNativeMessage(message).catch(disconnectNative);
    });
    nativePort.onDisconnect.addListener(() => {
      handleNativePortDisconnect(chrome.runtime, disconnectNative);
    });
  } catch {
    disconnectNative();
    return false;
  }

  const timeout = setTimeout(() => disconnectNative(), HANDSHAKE_TIMEOUT_MILLISECONDS);
  const ready = await promise;
  clearTimeout(timeout);
  return ready;
}

async function handleNativeMessage(message) {
  if (message?.type === 'hostHello') {
    if (connectionId) {
      throw new Error('Native Host hello was repeated.');
    }
    const hello = validateHostHello(message);
    const profileId = await getProfileId();
    const extensionNonce = crypto.randomUUID();
    const browserFamily = await detectBrowserFamily();
    const capabilities = hello.capabilities.filter((value) => CAPABILITIES.includes(value));
    if (capabilities.length === 0) {
      throw new Error('Native Host and Extension have no shared capabilities.');
    }
    handshake.expected = Object.freeze({
      extensionId: chrome.runtime.id,
      hostNonce: hello.hostNonce,
      extensionNonce,
      browserFamily,
      profileId,
      capabilities,
    });
    nativePort.postMessage({
      protocolVersion: 2,
      type: 'extensionHello',
      ...handshake.expected,
    });
    return;
  }

  if (message?.type === 'helloAck') {
    if (!handshake?.expected || connectionId) {
      throw new Error('Unexpected Native Host acknowledgement.');
    }
    const accepted = await validateHelloAck(message, handshake.expected);
    connectionId = accepted.connectionId;
    negotiatedCapabilities = accepted.capabilities;
    inboundCommandGuard = createInboundCommandGuard();
    handshake.resolve(true);
    for (const target of authorizedTargetLifecycle.values()) {
      await publishTarget(target);
    }
    return;
  }

  if (message?.type === 'command') {
    if (!connectionId || !inboundCommandGuard) {
      throw new Error('Native command arrived before negotiation.');
    }
    const request = inboundCommandGuard.validate(
      message,
      connectionId,
      negotiatedCapabilities,
    );
    const result = await dispatchBoundCommand({
      tabs: chrome.tabs,
      documentRegistry: { matches: () => false },
      genericTargetRegistry,
      request,
    });
    postMessage({
      protocolVersion: 2,
      type: 'commandResult',
      connectionId,
      sequence: nextOutboundSequence(),
      requestId: request.requestId,
      accepted: result?.accepted === true,
      errorCode: result?.accepted === true
        ? null
        : normalizeError(result?.errorCode),
    });
    if (result?.accepted === true && result.presentation) {
      const current = authorizedTargetLifecycle.get(request.target.tabId);
      if (current?.target.bindingId === request.target.bindingId
          && current.target.endpointId === request.target.endpointId) {
        await authorizedTargetLifecycle.observe(request.target, result.presentation);
      }
    }
    return;
  }

  if (message?.type === 'revoke') {
    if (!connectionId || !inboundCommandGuard) {
      throw new Error('Native revoke arrived before negotiation.');
    }
    const request = inboundCommandGuard.validateRevoke(message, connectionId);
    const matches = [...authorizedTargetLifecycle.values()]
      .filter((entry) => entry.target.bindingId === request.bindingId);
    let revoked = false;
    if (matches.length === 1) {
      const [entry] = matches;
      if (entry.target.scope === 'site') {
        await chrome.permissions.remove({ origins: [`${entry.target.pageOrigin}/*`] });
      }
      await revokeTarget(entry, 'permission-revoked');
      revoked = true;
    }
    postMessage({
      protocolVersion: 2,
      type: 'revokeResult',
      connectionId,
      sequence: nextOutboundSequence(),
      requestId: request.requestId,
      revoked,
    });
    return;
  }

  throw new Error('Native Host message type is unsupported.');
}

async function publishTarget(entry) {
  if (!connectionId) {
    throw new Error('Native Host is unavailable.');
  }
  postMessage({
    protocolVersion: 2,
    type: 'targetSnapshot',
    connectionId,
    sequence: nextOutboundSequence(),
    target: entry.target,
    presentation: normalizePresentation(entry.presentation),
  });
}

function removeTarget(tabId, reason) {
  return authorizedTargetLifecycle.remove(tabId, reason);
}

async function revokeTarget(entry, reason) {
  try {
    await chrome.tabs.sendMessage(
      entry.target.tabId,
      { type: 'unbindGenericEndpoint', target: entry.target },
      { documentId: entry.target.documentId },
    );
  } catch {
    // The exact document may already be gone; registry removal is still required.
  }
  return removeTarget(entry.target.tabId, reason);
}

async function handlePresentationChanged(message, sender) {
  if (!hasExactFields(message, ['type', 'target', 'presentation'])
      || !hasExactFields(message.target, [
        'bindingId',
        'endpointId',
        'scope',
        'tabId',
        'frameId',
        'documentId',
        'pageOrigin',
      ])
      || sender?.tab?.id !== message.target?.tabId
      || sender?.frameId !== 0
      || sender?.documentId !== message.target?.documentId) {
    return false;
  }
  return authorizedTargetLifecycle.observe(
    message.target,
    normalizePresentation(message.presentation),
  );
}

function publishTargetRemoved(entry, reason) {
  if (connectionId) {
    postMessage({
      protocolVersion: 2,
      type: 'targetRemoved',
      connectionId,
      sequence: nextOutboundSequence(),
      bindingId: entry.target.bindingId,
      reason: normalizeError(reason),
    });
  }
}

function normalizePresentation(value) {
  if (!hasExactFields(value, [
    'sourceDisplayName',
    'playbackStatus',
    'playbackRate',
    'capabilities',
    'observedAt',
    'timeline',
  ])) {
    throw new Error('Authorized target presentation schema is invalid.');
  }
  const sourceDisplayName = typeof value?.sourceDisplayName === 'string'
    ? value.sourceDisplayName.slice(0, 256)
    : 'Authorized web media';
  const playbackStatus = ['playing', 'paused', 'stopped', 'changing']
    .includes(value?.playbackStatus)
    ? value.playbackStatus
    : 'unknown';
  if (!Number.isFinite(value?.playbackRate)
      || value.playbackRate < 0
      || value.playbackRate > 16) {
    throw new Error('Authorized target playback rate is invalid.');
  }
  const capabilities = Array.isArray(value?.capabilities)
    ? [...new Set(value.capabilities.filter((item) => CAPABILITIES.includes(item)))].sort()
    : [];
  if (capabilities.length === 0) {
    throw new Error('Authorized target has no supported capabilities.');
  }
  const timeline = value?.timeline === null
    ? null
    : normalizeTimeline(value?.timeline);
  return Object.freeze({
    sourceDisplayName,
    playbackStatus,
    playbackRate: value.playbackRate,
    capabilities,
    observedAt: new Date(value?.observedAt).toISOString(),
    timeline,
  });
}

function normalizeTimeline(value) {
  if (!hasExactFields(value, ['startSeconds', 'endSeconds', 'positionSeconds'])
      || !Number.isFinite(value?.startSeconds)
      || !Number.isFinite(value?.endSeconds)
      || !Number.isFinite(value?.positionSeconds)
      || value.startSeconds < 0
      || value.endSeconds <= value.startSeconds
      || value.positionSeconds < value.startSeconds
      || value.positionSeconds > value.endSeconds) {
    throw new Error('Authorized target timeline is invalid.');
  }
  return Object.freeze({
    startSeconds: value.startSeconds,
    endSeconds: value.endSeconds,
    positionSeconds: value.positionSeconds,
  });
}

function hasExactFields(value, expected) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }
  const actual = Object.keys(value).sort();
  const sortedExpected = [...expected].sort();
  return actual.length === sortedExpected.length
    && actual.every((field, index) => field === sortedExpected[index]);
}

function postMessage(message) {
  if (!nativePort || !connectionId) {
    throw new Error('Native Host is unavailable.');
  }
  nativePort.postMessage(message);
}

function nextOutboundSequence() {
  outboundSequence += 1;
  return outboundSequence;
}

function disconnectNative({ disconnectPort = true } = {}) {
  if (disconnectPort) {
    try {
      nativePort?.disconnect();
    } catch {
      nativePort = undefined;
    }
  }
  nativePort = undefined;
  connectionId = undefined;
  negotiatedCapabilities = [];
  inboundCommandGuard = undefined;
  outboundSequence = 0;
  handshake?.resolve(false);
  handshake = undefined;
}

async function getProfileId() {
  const existing = await chrome.storage.local.get('profileId');
  if (typeof existing.profileId === 'string') {
    return existing.profileId;
  }
  const profileId = crypto.randomUUID();
  await chrome.storage.local.set({ profileId });
  return profileId;
}

async function detectBrowserFamily() {
  if (typeof navigator.brave?.isBrave === 'function' && await navigator.brave.isBrave()) {
    return 'brave';
  }
  return 'chrome';
}

function normalizeError(value) {
  const allowed = new Set([
    'ambiguous-media-elements',
    'document-replaced',
    'media-element-unavailable',
    'permission-denied',
    'permission-revoked',
    'play-rejected',
    'seek-out-of-range',
    'tab-closed',
    'target-unavailable',
    'target-replaced',
    'unauthorized-command',
  ]);
  return allowed.has(value) ? value : 'target-unavailable';
}
