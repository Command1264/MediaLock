import test from 'node:test';
import assert from 'node:assert/strict';

import {
  createNativeHostReadinessGate,
  createReplayGuard,
  validateHelloAck,
  validateNativeCommand,
} from '../protocol.mjs';

const sessionId = '11111111-1111-4111-8111-111111111111';
const requestId = '22222222-2222-4222-8222-222222222222';

function validCommand(overrides = {}) {
  return {
    protocolVersion: 1,
    type: 'command',
    sessionId,
    sequence: 1,
    requestId,
    target: {
      tabId: 42,
      frameId: 0,
      pageOrigin: 'https://music.youtube.com',
    },
    command: { name: 'pause' },
    ...overrides,
  };
}

test('accepts one exact top-frame YouTube Music command', () => {
  const guard = createReplayGuard(32);
  const command = validateNativeCommand(validCommand(), sessionId, guard);

  assert.equal(command.target.tabId, 42);
  assert.equal(command.command.name, 'pause');
});

test('accepts only the matching hello acknowledgement', () => {
  assert.deepEqual(validateHelloAck({
    protocolVersion: 1,
    type: 'helloAck',
    sessionId,
  }, sessionId), { sessionId });

  assert.throws(() => validateHelloAck({
    protocolVersion: 1,
    type: 'helloAck',
    sessionId: crypto.randomUUID(),
  }, sessionId), /session/i);
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
  validateNativeCommand(validCommand(), sessionId, guard);

  assert.throws(
    () => validateNativeCommand(validCommand({ sequence: 2 }), sessionId, guard),
    /replay/i,
  );
});

test('rejects duplicate or out-of-order sequence numbers', () => {
  const guard = createReplayGuard(32);
  validateNativeCommand(validCommand(), sessionId, guard);

  assert.throws(
    () => validateNativeCommand(
      validCommand({ sequence: 1, requestId: crypto.randomUUID() }),
      sessionId,
      guard,
    ),
    /sequence/i,
  );
  assert.throws(
    () => validateNativeCommand(
      validCommand({ sequence: 0, requestId: crypto.randomUUID() }),
      sessionId,
      guard,
    ),
    /sequence/i,
  );
});

test('rejects a stale session nonce', () => {
  assert.throws(
    () => validateNativeCommand(validCommand(), crypto.randomUUID(), createReplayGuard(32)),
    /session/i,
  );
});

test('rejects unknown fields instead of silently accepting a new schema', () => {
  assert.throws(
    () => validateNativeCommand(validCommand({ admin: true }), sessionId, createReplayGuard(32)),
    /field/i,
  );
});

test('rejects non-top frames and unapproved page origins', () => {
  const wrongFrame = validCommand({
    target: { tabId: 42, frameId: 1, pageOrigin: 'https://music.youtube.com' },
  });
  const wrongOrigin = validCommand({
    target: { tabId: 42, frameId: 0, pageOrigin: 'https://example.com' },
  });

  assert.throws(() => validateNativeCommand(wrongFrame, sessionId, createReplayGuard(32)), /frame/i);
  assert.throws(() => validateNativeCommand(wrongOrigin, sessionId, createReplayGuard(32)), /origin/i);
});

test('accepts only the command allowlist and finite non-negative seek positions', () => {
  assert.throws(
    () => validateNativeCommand(validCommand({ command: { name: 'run-script' } }), sessionId, createReplayGuard(32)),
    /command/i,
  );
  assert.throws(
    () => validateNativeCommand(validCommand({ command: { name: 'seek', positionSeconds: -1 } }), sessionId, createReplayGuard(32)),
    /position/i,
  );
  assert.throws(
    () => validateNativeCommand(validCommand({ command: { name: 'seek', positionSeconds: Number.POSITIVE_INFINITY } }), sessionId, createReplayGuard(32)),
    /position/i,
  );

  const seek = validateNativeCommand(
    validCommand({ command: { name: 'seek', positionSeconds: 12.5 } }),
    sessionId,
    createReplayGuard(32),
  );
  assert.equal(seek.command.positionSeconds, 12.5);
});
