const PROTOCOL_VERSION = 1;
const ALLOWED_PAGE_ORIGINS = new Set([
  'https://www.youtube.com',
  'https://music.youtube.com',
]);
const ALLOWED_COMMANDS = new Set(['play', 'pause', 'seek']);
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const CONNECTION_ID_PATTERN = /^[0-9a-f]{64}$/;
const BROWSER_FAMILIES = new Set(['chrome', 'brave']);
const CAPABILITIES = new Set(['pause', 'play', 'seek']);

export async function deriveConnectionId(value) {
  requireUuid(value.hostNonce, 'host nonce');
  requireUuid(value.extensionNonce, 'extension nonce');
  if (typeof value.extensionId !== 'string' || value.extensionId.length === 0) {
    throw new Error('Extension ID is required.');
  }
  if (!BROWSER_FAMILIES.has(value.browserFamily)) {
    throw new Error('Browser family is not supported.');
  }
  const capabilities = validateCapabilities(value.capabilities);
  const canonical = [
    'phase16a',
    PROTOCOL_VERSION,
    value.extensionId,
    value.hostNonce,
    value.extensionNonce,
    value.browserFamily,
    capabilities.join(','),
  ].join('|');
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(canonical));
  return [...new Uint8Array(digest)]
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('');
}

export function createNativeHostReadinessGate(timeoutMilliseconds) {
  if (!Number.isSafeInteger(timeoutMilliseconds)
      || timeoutMilliseconds < 1
      || timeoutMilliseconds > 10000) {
    throw new TypeError('Native Host readiness timeout must be an integer from 1 through 10000.');
  }

  let ready = false;
  const waiters = new Set();

  function settle(waiter, result) {
    if (!waiters.delete(waiter)) {
      return;
    }
    clearTimeout(waiter.timeoutId);
    waiter.resolve(result);
  }

  return Object.freeze({
    waitUntilReady() {
      if (ready) {
        return Promise.resolve(true);
      }

      return new Promise((resolve) => {
        const waiter = { resolve, timeoutId: undefined };
        waiters.add(waiter);
        waiter.timeoutId = setTimeout(() => settle(waiter, false), timeoutMilliseconds);
      });
    },
    markReady() {
      ready = true;
      for (const waiter of [...waiters]) {
        settle(waiter, true);
      }
    },
    reset() {
      ready = false;
      for (const waiter of [...waiters]) {
        settle(waiter, false);
      }
    },
  });
}

export function createReplayGuard(capacity) {
  if (!Number.isSafeInteger(capacity) || capacity < 1 || capacity > 4096) {
    throw new TypeError('Replay guard capacity must be an integer from 1 through 4096.');
  }

  const observed = new Set();
  const insertionOrder = [];
  let lastSequence = 0;
  return Object.freeze({
    observe(sequence, requestId) {
      if (!Number.isSafeInteger(sequence) || sequence !== lastSequence + 1) {
        throw new Error('Native command sequence is not strictly monotonic.');
      }
      if (observed.has(requestId)) {
        throw new Error('Native command replay detected.');
      }

      lastSequence = sequence;
      observed.add(requestId);
      insertionOrder.push(requestId);
      if (insertionOrder.length > capacity) {
        observed.delete(insertionOrder.shift());
      }
    },
  });
}

export function validateHostHello(value) {
  requirePlainObject(value, 'Host hello');
  requireExactFields(value, ['protocolVersion', 'type', 'hostNonce', 'capabilities']);
  if (value.protocolVersion !== PROTOCOL_VERSION || value.type !== 'hostHello') {
    throw new Error('Unsupported Native Host protocol.');
  }
  requireUuid(value.hostNonce, 'host nonce');
  const capabilities = validateCapabilities(value.capabilities);
  return Object.freeze({ hostNonce: value.hostNonce, capabilities });
}

export async function validateHelloAck(value, expected) {
  requirePlainObject(value, 'Hello acknowledgement');
  requireExactFields(value, [
    'protocolVersion',
    'type',
    'hostNonce',
    'extensionNonce',
    'connectionId',
    'browserFamily',
    'capabilities',
  ]);
  if (value.protocolVersion !== PROTOCOL_VERSION || value.type !== 'helloAck') {
    throw new Error('Unsupported Native Host hello acknowledgement.');
  }
  if (value.hostNonce !== expected.hostNonce || value.extensionNonce !== expected.extensionNonce) {
    throw new Error('Hello acknowledgement nonce does not match the active connection.');
  }
  if (value.browserFamily !== expected.browserFamily) {
    throw new Error('Hello acknowledgement browser family does not match.');
  }
  const capabilities = validateCapabilities(value.capabilities);
  if (capabilities.join(',') !== validateCapabilities(expected.capabilities).join(',')) {
    throw new Error('Hello acknowledgement capabilities do not match.');
  }
  if (!CONNECTION_ID_PATTERN.test(value.connectionId)) {
    throw new Error('Connection ID is invalid.');
  }
  const derived = await deriveConnectionId(expected);
  if (value.connectionId !== derived) {
    throw new Error('Hello acknowledgement connection ID does not match.');
  }
  return Object.freeze({ connectionId: value.connectionId, capabilities });
}

export function validateNativeCommand(
  value,
  expectedConnectionId,
  replayGuard,
  negotiatedCapabilities,
) {
  requirePlainObject(value, 'Native command');
  requireExactFields(value, [
    'protocolVersion',
    'type',
    'connectionId',
    'sequence',
    'requestId',
    'target',
    'command',
  ]);
  if (value.protocolVersion !== PROTOCOL_VERSION || value.type !== 'command') {
    throw new Error('Unsupported Native command protocol.');
  }
  if (value.connectionId !== expectedConnectionId) {
    throw new Error('Native command connection does not match the active connection.');
  }
  if (!CONNECTION_ID_PATTERN.test(value.connectionId)) {
    throw new Error('Native command connection ID is invalid.');
  }
  requireUuid(value.requestId, 'request');

  const target = validateTarget(value.target);
  const command = validateCommand(value.command);
  if (!validateCapabilities(negotiatedCapabilities).includes(command.name)) {
    throw new Error('Media command was not negotiated for this connection.');
  }
  replayGuard.observe(value.sequence, value.requestId);
  return Object.freeze({
    protocolVersion: PROTOCOL_VERSION,
    type: 'command',
    connectionId: value.connectionId,
    sequence: value.sequence,
    requestId: value.requestId,
    target,
    command,
  });
}

function validateCapabilities(value) {
  if (!Array.isArray(value) || value.length === 0) {
    throw new Error('Capabilities must be a non-empty array.');
  }
  const sorted = [...value].sort();
  if (new Set(sorted).size !== sorted.length || sorted.some((item) => !CAPABILITIES.has(item))) {
    throw new Error('Capabilities contain duplicates or unsupported values.');
  }
  return Object.freeze(sorted);
}

function validateTarget(value) {
  requirePlainObject(value, 'Command target');
  requireExactFields(value, ['tabId', 'frameId', 'documentId', 'pageOrigin']);
  if (!Number.isSafeInteger(value.tabId) || value.tabId < 0) {
    throw new Error('Command target tab ID is invalid.');
  }
  if (value.frameId !== 0) {
    throw new Error('Only the top frame can receive a browser media command.');
  }
  requireUuid(value.documentId, 'document');
  if (!ALLOWED_PAGE_ORIGINS.has(value.pageOrigin)) {
    throw new Error('Command target page origin is not authorized.');
  }
  return Object.freeze({
    tabId: value.tabId,
    frameId: value.frameId,
    documentId: value.documentId,
    pageOrigin: value.pageOrigin,
  });
}

function validateCommand(value) {
  requirePlainObject(value, 'Media command');
  if (!ALLOWED_COMMANDS.has(value.name)) {
    throw new Error('Media command is not allowed.');
  }
  const expectedFields = value.name === 'seek' ? ['name', 'positionSeconds'] : ['name'];
  requireExactFields(value, expectedFields);
  if (value.name === 'seek'
      && (!Number.isFinite(value.positionSeconds) || value.positionSeconds < 0)) {
    throw new Error('Seek position must be finite and non-negative.');
  }
  return Object.freeze(value.name === 'seek'
    ? { name: value.name, positionSeconds: value.positionSeconds }
    : { name: value.name });
}

function requirePlainObject(value, label) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new TypeError(`${label} must be an object.`);
  }
}

function requireExactFields(value, expectedFields) {
  const actualFields = Object.keys(value).sort();
  const sortedExpected = [...expectedFields].sort();
  if (actualFields.length !== sortedExpected.length
      || actualFields.some((field, index) => field !== sortedExpected[index])) {
    throw new Error('Message contains a missing or unknown field.');
  }
}

function requireUuid(value, label) {
  if (typeof value !== 'string' || !UUID_PATTERN.test(value)) {
    throw new Error(`${label} ID must be a UUID.`);
  }
}
