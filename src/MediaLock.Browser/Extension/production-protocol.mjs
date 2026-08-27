const PROTOCOL_VERSION = 2;
const CAPABILITIES = new Set(['pause', 'play', 'seek']);
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const CONNECTION_ID_PATTERN = /^[0-9a-f]{64}$/;
const OPAQUE_PATTERN = /^[\x21-\x7e]+$/;

export async function deriveConnectionId(value) {
  requireUuid(value.hostNonce, 'host nonce');
  requireUuid(value.extensionNonce, 'extension nonce');
  requireUuid(value.profileId, 'profile identity');
  const capabilities = validateCapabilities(value.capabilities);
  const canonical = [
    'medialock.browser-direct.v2',
    value.extensionId,
    value.hostNonce,
    value.extensionNonce,
    value.browserFamily,
    value.profileId,
    capabilities.join(','),
  ].join('\n');
  const digest = await crypto.subtle.digest(
    'SHA-256',
    new TextEncoder().encode(canonical),
  );
  return [...new Uint8Array(digest)]
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('');
}

export function validateHostHello(value) {
  requirePlainObject(value, 'Host hello');
  requireExactFields(value, ['protocolVersion', 'type', 'hostNonce', 'capabilities']);
  if (value.protocolVersion !== PROTOCOL_VERSION || value.type !== 'hostHello') {
    throw new Error('Unsupported Native Host protocol.');
  }
  requireUuid(value.hostNonce, 'host nonce');
  return Object.freeze({
    hostNonce: value.hostNonce,
    capabilities: validateCapabilities(value.capabilities),
  });
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
    'profileId',
    'capabilities',
  ]);
  if (value.protocolVersion !== PROTOCOL_VERSION
      || value.type !== 'helloAck'
      || value.hostNonce !== expected.hostNonce
      || value.extensionNonce !== expected.extensionNonce
      || value.browserFamily !== expected.browserFamily
      || value.profileId !== expected.profileId
      || !CONNECTION_ID_PATTERN.test(value.connectionId)) {
    throw new Error('Native Host acknowledgement does not match this connection.');
  }
  const capabilities = validateCapabilities(value.capabilities);
  if (capabilities.join(',') !== validateCapabilities(expected.capabilities).join(',')) {
    throw new Error('Negotiated Browser capabilities do not match.');
  }
  if (value.connectionId !== await deriveConnectionId(expected)) {
    throw new Error('Native Host connection identity is invalid.');
  }
  return Object.freeze({ connectionId: value.connectionId, capabilities });
}

export function createInboundCommandGuard() {
  let lastSequence = 0;
  const observed = new Set();
  const order = [];
  function observe(value, expectedConnectionId, type, fields) {
    requirePlainObject(value, `Native ${type}`);
    requireExactFields(value, fields);
    if (value.protocolVersion !== PROTOCOL_VERSION
        || value.type !== type
        || value.connectionId !== expectedConnectionId
        || !Number.isSafeInteger(value.sequence)
        || value.sequence !== lastSequence + 1) {
      throw new Error(`Native ${type} envelope is stale or invalid.`);
    }
    requireUuid(value.requestId, 'request');
    if (observed.has(value.requestId)) {
      throw new Error(`Native ${type} replay detected.`);
    }
    lastSequence = value.sequence;
    observed.add(value.requestId);
    order.push(value.requestId);
    if (order.length > 1024) {
      observed.delete(order.shift());
    }
  }
  return Object.freeze({
    validate(value, expectedConnectionId, negotiatedCapabilities) {
      observe(value, expectedConnectionId, 'command', [
        'protocolVersion',
        'type',
        'connectionId',
        'sequence',
        'requestId',
        'target',
        'command',
      ]);
      const target = validateTarget(value.target);
      const command = validateCommand(value.command);
      if (!validateCapabilities(negotiatedCapabilities).includes(command.name)) {
        throw new Error('Native command capability was not negotiated.');
      }
      return Object.freeze({ ...value, target, command });
    },
    validateRevoke(value, expectedConnectionId) {
      observe(value, expectedConnectionId, 'revoke', [
        'protocolVersion',
        'type',
        'connectionId',
        'sequence',
        'requestId',
        'bindingId',
      ]);
      requireOpaque(value.bindingId, 128, 'Page Binding');
      return Object.freeze({ ...value });
    },
  });
}

function validateTarget(value) {
  requirePlainObject(value, 'Browser target');
  requireExactFields(value, [
    'bindingId',
    'endpointId',
    'scope',
    'tabId',
    'frameId',
    'documentId',
    'pageOrigin',
  ]);
  requireOpaque(value.bindingId, 128, 'Page Binding');
  requireOpaque(value.endpointId, 128, 'Browser Media Endpoint');
  requireOpaque(value.documentId, 256, 'document identity');
  if ((value.scope !== 'temporary' && value.scope !== 'site')
      || !Number.isSafeInteger(value.tabId)
      || value.tabId < 0
      || value.frameId !== 0
      || !isExactHttpsOrigin(value.pageOrigin)) {
    throw new Error('Browser target authority is invalid.');
  }
  return Object.freeze({ ...value });
}

function validateCommand(value) {
  requirePlainObject(value, 'Media command');
  if (!CAPABILITIES.has(value.name)) {
    throw new Error('Media command is not allowed.');
  }
  if (value.name === 'seek') {
    requireExactFields(value, ['name', 'positionSeconds']);
    if (!Number.isFinite(value.positionSeconds) || value.positionSeconds < 0) {
      throw new Error('Seek position is invalid.');
    }
  } else {
    requireExactFields(value, ['name']);
  }
  return Object.freeze({ ...value });
}

function validateCapabilities(value) {
  if (!Array.isArray(value) || value.length < 1 || value.length > 3) {
    throw new Error('Capabilities are invalid.');
  }
  const sorted = [...value].sort();
  if (new Set(sorted).size !== sorted.length
      || sorted.some((capability) => !CAPABILITIES.has(capability))) {
    throw new Error('Capabilities contain duplicate or unsupported values.');
  }
  return Object.freeze(sorted);
}

function requirePlainObject(value, label) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${label} must be an object.`);
  }
}

function requireExactFields(value, expected) {
  const actual = Object.keys(value).sort();
  const sortedExpected = [...expected].sort();
  if (actual.length !== sortedExpected.length
      || actual.some((field, index) => field !== sortedExpected[index])) {
    throw new Error('Protocol message contains missing or unknown fields.');
  }
}

function requireUuid(value, label) {
  if (typeof value !== 'string' || !UUID_PATTERN.test(value)
      || value === '00000000-0000-0000-0000-000000000000') {
    throw new Error(`${label} must be a non-empty UUID.`);
  }
}

function requireOpaque(value, maximumLength, label) {
  if (typeof value !== 'string' || value.length < 1 || value.length > maximumLength
      || !OPAQUE_PATTERN.test(value)) {
    throw new Error(`${label} is invalid.`);
  }
}

function isExactHttpsOrigin(value) {
  try {
    const parsed = new URL(value);
    return parsed.protocol === 'https:' && parsed.origin === value;
  } catch {
    return false;
  }
}
