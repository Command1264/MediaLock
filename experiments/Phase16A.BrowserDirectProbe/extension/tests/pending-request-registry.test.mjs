import test from 'node:test';
import assert from 'node:assert/strict';

import { createPendingRequestRegistry } from '../pending-request-registry.mjs';
import { completePendingRequest } from '../request-completion.mjs';

test('settles one timed-out request once and rejects a late Host result', async () => {
  const requestId = '22222222-2222-4222-8222-222222222222';
  let settlementCount = 0;
  const timedOutRequestIds = [];
  const registry = createPendingRequestRegistry(
    64,
    5,
    (timedOutRequestId) => timedOutRequestIds.push(timedOutRequestId),
  );

  const outcome = registry.add(requestId).then((value) => {
    settlementCount += 1;
    return value;
  });

  assert.equal(registry.claim(requestId), true);
  assert.equal(registry.claim(requestId), false);

  assert.deepEqual(await outcome, { accepted: false, errorCode: 'outcome-unknown' });
  assert.equal(registry.complete(requestId, { accepted: true, errorCode: null }), false);
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.equal(settlementCount, 1);
  assert.deepEqual(timedOutRequestIds, [requestId]);
});

test('keeps the deadline active while claimed and cancels it only on completion', async () => {
  const timedOutRequestIds = [];
  const registry = createPendingRequestRegistry(
    2,
    20,
    (requestId) => timedOutRequestIds.push(requestId),
  );
  const outcome = registry.add('request-2');

  assert.equal(registry.claim('request-2'), true);
  assert.equal(registry.complete('request-2', { accepted: true, errorCode: null }), true);

  assert.deepEqual(await outcome, { accepted: true, errorCode: null });
  await new Promise((resolve) => setTimeout(resolve, 30));
  assert.deepEqual(timedOutRequestIds, []);
});

test('keeps a claimed request pending when Host result delivery throws', async () => {
  const registry = createPendingRequestRegistry(2, 100);
  const outcome = registry.add('request-3');
  assert.equal(registry.claim('request-3'), true);

  assert.throws(() => completePendingRequest({
    pendingRequests: registry,
    requestId: 'request-3',
    outcome: { accepted: true, errorCode: null },
    sendResult: () => { throw new Error('Native Host disconnected.'); },
  }), /disconnected/i);
  assert.equal(registry.size, 1);

  registry.reset({ accepted: false, errorCode: 'native-host-disconnected' });
  assert.deepEqual(await outcome, {
    accepted: false,
    errorCode: 'native-host-disconnected',
  });
});
