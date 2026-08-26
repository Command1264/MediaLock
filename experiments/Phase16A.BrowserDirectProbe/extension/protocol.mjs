const PROTOCOL_VERSION = 1;
const ALLOWED_PAGE_ORIGINS = new Set([
  'https://www.youtube.com',
  'https://music.youtube.com',
]);
const ALLOWED_COMMANDS = new Set(['play', 'pause', 'seek']);
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

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
  requireExactFields(value, ['protocolVersion', 'type', 'sessionId']);
  if (value.protocolVersion !== PROTOCOL_VERSION || value.type !== 'hostHello') {
    throw new Error('Unsupported Native Host protocol.');
  }
  requireUuid(value.sessionId, 'session');
  return Object.freeze({ sessionId: value.sessionId });
}

export function validateHelloAck(value, expectedSessionId) {
  requirePlainObject(value, 'Hello acknowledgement');
  requireExactFields(value, ['protocolVersion', 'type', 'sessionId']);
  if (value.protocolVersion !== PROTOCOL_VERSION || value.type !== 'helloAck') {
    throw new Error('Unsupported Native Host hello acknowledgement.');
  }
  if (value.sessionId !== expectedSessionId) {
    throw new Error('Hello acknowledgement session does not match the active host session.');
  }
  requireUuid(value.sessionId, 'session');
  return { sessionId: value.sessionId };
}

export function validateNativeCommand(value, expectedSessionId, replayGuard) {
  requirePlainObject(value, 'Native command');
  requireExactFields(value, [
    'protocolVersion',
    'type',
    'sessionId',
    'sequence',
    'requestId',
    'target',
    'command',
  ]);
  if (value.protocolVersion !== PROTOCOL_VERSION || value.type !== 'command') {
    throw new Error('Unsupported Native command protocol.');
  }
  if (value.sessionId !== expectedSessionId) {
    throw new Error('Native command session does not match the active host session.');
  }
  requireUuid(value.sessionId, 'session');
  requireUuid(value.requestId, 'request');

  const target = validateTarget(value.target);
  const command = validateCommand(value.command);
  replayGuard.observe(value.sequence, value.requestId);
  return Object.freeze({
    protocolVersion: PROTOCOL_VERSION,
    type: 'command',
    sessionId: value.sessionId,
    sequence: value.sequence,
    requestId: value.requestId,
    target,
    command,
  });
}

function validateTarget(value) {
  requirePlainObject(value, 'Command target');
  requireExactFields(value, ['tabId', 'frameId', 'pageOrigin']);
  if (!Number.isSafeInteger(value.tabId) || value.tabId < 0) {
    throw new Error('Command target tab ID is invalid.');
  }
  if (value.frameId !== 0) {
    throw new Error('Only the top frame can receive a browser media command.');
  }
  if (!ALLOWED_PAGE_ORIGINS.has(value.pageOrigin)) {
    throw new Error('Command target page origin is not authorized.');
  }
  return Object.freeze({
    tabId: value.tabId,
    frameId: value.frameId,
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
