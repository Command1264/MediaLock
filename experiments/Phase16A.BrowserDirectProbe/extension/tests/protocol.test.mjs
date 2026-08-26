import test from 'node:test';
import assert from 'node:assert/strict';

import {
  createNativeHostReadinessGate,
  createReplayGuard,
  deriveConnectionId,
  validateHelloAck,
  validateHostHello,
  validateNativeCommand,
} from '../protocol.mjs';

const hostNonce = '11111111-1111-4111-8111-111111111111';
const extensionNonce = '55555555-5555-4555-8555-555555555555';
const connectionId = '2e9cfd93293ebe38ff70df4df25986f814b6c1b1ee52d345aa558bb3884f5223';
const requestId = '22222222-2222-4222-8222-222222222222';
const documentId = 'ABCDEF0123456789ABCDEF0123456789';
const capabilities = ['pause', 'play', 'seek'];

function validCommand(overrides = {}) {
  return {
    protocolVersion: 1,
    type: 'command',
    connectionId,
    sequence: 1,
    requestId,
    target: {
      tabId: 42,
      frameId: 0,
      documentId,
      pageOrigin: 'https://music.youtube.com',
    },
    command: { name: 'pause' },
    ...overrides,
  };
}

test('accepts one exact top-frame YouTube Music command', () => {
  const guard = createReplayGuard(32);
  const command = validateNativeCommand(
    validCommand(),
    connectionId,
    guard,
    capabilities,
  );

  assert.equal(command.target.tabId, 42);
  assert.equal(command.command.name, 'pause');
});

test('rejects a command that was not negotiated for this connection', () => {
  assert.throws(() => validateNativeCommand(
    validCommand({ command: { name: 'play' } }),
    connectionId,
    createReplayGuard(32),
    ['pause'],
  ), /negotiated/i);
});

test('derives the same connection ID from both nonces and negotiated metadata', async () => {
  assert.equal(await deriveConnectionId({
    extensionId: 'abcdefghijklmnopabcdefghijklmnop',
    hostNonce,
    extensionNonce,
    browserFamily: 'brave',
    capabilities: ['seek', 'pause', 'play'],
  }), connectionId);
});

test('rejects unsupported or duplicate Host capabilities', () => {
  assert.throws(() => validateHostHello({
    protocolVersion: 1,
    type: 'hostHello',
    hostNonce,
    capabilities: ['play', 'run-script'],
  }), /capabilit/i);
  assert.throws(() => validateHostHello({
    protocolVersion: 1,
    type: 'hostHello',
    hostNonce,
    capabilities: ['play', 'play'],
  }), /capabilit/i);
});

test('accepts only the matching hello acknowledgement', async () => {
  const acknowledgement = await validateHelloAck({
    protocolVersion: 1,
    type: 'helloAck',
    hostNonce,
    extensionNonce,
    connectionId,
    browserFamily: 'brave',
    capabilities: ['pause', 'play', 'seek'],
  }, {
    extensionId: 'abcdefghijklmnopabcdefghijklmnop',
    hostNonce,
    extensionNonce,
    browserFamily: 'brave',
    capabilities: ['pause', 'play', 'seek'],
  });
  assert.equal(acknowledgement.connectionId, connectionId);

  await assert.rejects(() => validateHelloAck({
    protocolVersion: 1,
    type: 'helloAck',
    hostNonce: crypto.randomUUID(),
    extensionNonce,
    connectionId,
    browserFamily: 'brave',
    capabilities: ['pause', 'play', 'seek'],
  }, {
    extensionId: 'abcdefghijklmnopabcdefghijklmnop',
    hostNonce,
    extensionNonce,
    browserFamily: 'brave',
    capabilities: ['pause', 'play', 'seek'],
  }), /nonce/i);
});

test('waits for initial Native Host negotiation before allowing one command', async () => {
  const gate = createNativeHostReadinessGate(100);
  let dispatchCount = 0;
  const waiting = gate.waitUntilReady().then((ready) => {
    if (ready) {
      dispatchCount += 1;
    }
    return ready;
  });

  gate.markReady();

  assert.equal(await waiting, true);
  assert.equal(dispatchCount, 1);
  assert.equal(await gate.waitUntilReady(), true);
});

test('fails a pre-dispatch readiness wait on timeout or disconnect', async () => {
  const timeoutGate = createNativeHostReadinessGate(1);
  assert.equal(await timeoutGate.waitUntilReady(), false);

  const disconnectedGate = createNativeHostReadinessGate(100);
  const waiting = disconnectedGate.waitUntilReady();
  disconnectedGate.reset();
  assert.equal(await waiting, false);
});

test('rejects replayed request IDs in one native-host session', () => {
  const guard = createReplayGuard(32);
  validateNativeCommand(validCommand(), connectionId, guard, capabilities);

  assert.throws(
    () => validateNativeCommand(validCommand({ sequence: 2 }), connectionId, guard, capabilities),
    /replay/i,
  );
});

test('rejects duplicate or out-of-order sequence numbers', () => {
  const guard = createReplayGuard(32);
  validateNativeCommand(validCommand(), connectionId, guard, capabilities);

  assert.throws(
    () => validateNativeCommand(
      validCommand({ sequence: 1, requestId: crypto.randomUUID() }),
      connectionId,
      guard,
      capabilities,
    ),
    /sequence/i,
  );
  assert.throws(
    () => validateNativeCommand(
      validCommand({ sequence: 0, requestId: crypto.randomUUID() }),
      connectionId,
      guard,
      capabilities,
    ),
    /sequence/i,
  );
});

test('rejects a stale connection ID', () => {
  assert.throws(
    () => validateNativeCommand(validCommand(), 'f'.repeat(64), createReplayGuard(32), capabilities),
    /connection/i,
  );
});

test('rejects unknown fields instead of silently accepting a new schema', () => {
  assert.throws(
    () => validateNativeCommand(validCommand({ admin: true }), connectionId, createReplayGuard(32), capabilities),
    /field/i,
  );
});

test('rejects non-top frames and unapproved page origins', () => {
  const wrongFrame = validCommand({
    target: { tabId: 42, frameId: 1, documentId, pageOrigin: 'https://music.youtube.com' },
  });
  const wrongOrigin = validCommand({
    target: { tabId: 42, frameId: 0, documentId, pageOrigin: 'https://example.com' },
  });

  assert.throws(() => validateNativeCommand(wrongFrame, connectionId, createReplayGuard(32), capabilities), /frame/i);
  assert.throws(() => validateNativeCommand(wrongOrigin, connectionId, createReplayGuard(32), capabilities), /origin/i);
});

test('rejects a missing or malformed browser document binding', () => {
  const missingDocument = validCommand({
    target: { tabId: 42, frameId: 0, pageOrigin: 'https://music.youtube.com' },
  });
  const malformedDocument = validCommand({
    target: {
      tabId: 42,
      frameId: 0,
      documentId: 'line\nbreak',
      pageOrigin: 'https://music.youtube.com',
    },
  });

  assert.throws(() => validateNativeCommand(missingDocument, connectionId, createReplayGuard(32), capabilities), /field/i);
  assert.throws(() => validateNativeCommand(malformedDocument, connectionId, createReplayGuard(32), capabilities), /document/i);
});

test('accepts only the command allowlist and finite non-negative seek positions', () => {
  assert.throws(
    () => validateNativeCommand(validCommand({ command: { name: 'run-script' } }), connectionId, createReplayGuard(32), capabilities),
    /command/i,
  );
  assert.throws(
    () => validateNativeCommand(validCommand({ command: { name: 'seek', positionSeconds: -1 } }), connectionId, createReplayGuard(32), capabilities),
    /position/i,
  );
  assert.throws(
    () => validateNativeCommand(validCommand({ command: { name: 'seek', positionSeconds: Number.POSITIVE_INFINITY } }), connectionId, createReplayGuard(32), capabilities),
    /position/i,
  );

  const seek = validateNativeCommand(
    validCommand({ command: { name: 'seek', positionSeconds: 12.5 } }),
    connectionId,
    createReplayGuard(32),
    capabilities,
  );
  assert.equal(seek.command.positionSeconds, 12.5);
});
