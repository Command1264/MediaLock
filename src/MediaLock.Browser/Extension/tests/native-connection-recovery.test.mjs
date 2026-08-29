import test from 'node:test';
import assert from 'node:assert/strict';

import { createNativeConnectionRecovery } from '../native-connection-recovery.mjs';

test('failed recovery uses bounded backoff and an alarm wake-up until connection succeeds', async () => {
  const scheduled = [];
  const clearedTimers = [];
  const alarmCreates = [];
  const alarmClears = [];
  const attempts = [true, true, false];
  const recovery = createNativeConnectionRecovery({
    alarms: {
      create(name, info) { alarmCreates.push([name, info]); },
      async clear(name) { alarmClears.push(name); },
    },
    attempt: async () => attempts.shift(),
    reportFailure: assert.fail,
    schedule(callback, delay) {
      const handle = { callback, delay };
      scheduled.push(handle);
      return handle;
    },
    cancelSchedule(handle) { clearedTimers.push(handle); },
    retryDelaysMilliseconds: [1000, 2000, 5000],
  });

  recovery.request();
  const first = scheduled.shift();
  assert.equal(first.delay, 1000);
  await first.callback();
  const second = scheduled.shift();
  assert.equal(second.delay, 2000);
  await recovery.handleAlarm({ name: 'media-lock-native-recovery' });
  const third = scheduled.shift();
  assert.equal(third.delay, 5000);
  await third.callback();

  assert.deepEqual(attempts, []);
  assert.equal(alarmCreates.length, 3);
  assert.ok(alarmCreates.every(([, info]) => info.delayInMinutes === 0.5));
  assert.ok(alarmClears.length >= 1);
  assert.ok(clearedTimers.length >= 1);
});

test('requests coalesce and connected cancels all pending recovery work', async () => {
  const scheduled = [];
  const alarmClears = [];
  const recovery = createNativeConnectionRecovery({
    alarms: {
      create() {},
      async clear(name) { alarmClears.push(name); },
    },
    attempt: async () => true,
    reportFailure: assert.fail,
    schedule(callback, delay) {
      const handle = { callback, delay };
      scheduled.push(handle);
      return handle;
    },
    cancelSchedule(handle) { handle.cancelled = true; },
  });

  recovery.request();
  recovery.request();
  assert.equal(scheduled.length, 1);

  recovery.connected();
  assert.equal(scheduled[0].cancelled, true);
  assert.deepEqual(alarmClears, ['media-lock-native-recovery']);
});

test('a request arriving during reconciliation is not lost', async () => {
  let finishAttempt;
  const scheduled = [];
  const recovery = createNativeConnectionRecovery({
    alarms: {
      create() {},
      async clear() { return true; },
    },
    attempt: () => new Promise((resolve) => { finishAttempt = resolve; }),
    reportFailure: assert.fail,
    schedule(callback, delay) {
      const handle = { callback, delay };
      scheduled.push(handle);
      return handle;
    },
    cancelSchedule() {},
  });

  const initial = recovery.start();
  await Promise.resolve();
  recovery.request();
  finishAttempt(false);
  await initial;

  assert.equal(scheduled.length, 1);
  assert.equal(scheduled[0].delay, 1000);
});

test('a failed availability probe is reported before the bounded retry', async () => {
  const failures = [];
  const scheduled = [];
  const recovery = createNativeConnectionRecovery({
    alarms: {
      create() {},
      async clear() { return true; },
    },
    attempt: async () => { throw new TypeError('probe failed'); },
    reportFailure(error) { failures.push(error.name); },
    schedule(callback, delay) {
      const handle = { callback, delay };
      scheduled.push(handle);
      return handle;
    },
    cancelSchedule() {},
  });

  await recovery.start();

  assert.deepEqual(failures, ['TypeError']);
  assert.equal(scheduled.length, 1);
  assert.equal(scheduled[0].delay, 1000);
});
