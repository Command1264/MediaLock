import test from 'node:test';
import assert from 'node:assert/strict';

import { copyStatusText, createTransientFeedback } from '../status-copy.mjs';

test('copies the complete current status exactly once', async () => {
  const writes = [];
  const status = 'Accepted once by the active media element.';

  await copyStatusText(status, async (text) => {
    writes.push(text);
  });

  assert.deepEqual(writes, [status]);
});

test('rejects an empty status before accessing the clipboard', async () => {
  let writeCount = 0;

  await assert.rejects(
    copyStatusText('   ', async () => {
      writeCount += 1;
    }),
    /status/i,
  );

  assert.equal(writeCount, 0);
});

test('rejects a missing clipboard writer', async () => {
  await assert.rejects(
    copyStatusText('Ready.', undefined),
    /clipboard/i,
  );
});

test('clears copy feedback after the configured lifetime', () => {
  const shown = [];
  let hidden = 0;
  let scheduled;
  let scheduledDelay;
  const feedback = createTransientFeedback({
    show: (text) => shown.push(text),
    hide: () => { hidden += 1; },
    schedule: (callback, delay) => {
      scheduled = callback;
      scheduledDelay = delay;
      return 42;
    },
    cancel: () => {},
    durationMilliseconds: 2000,
  });

  feedback.show('Copied.');

  assert.deepEqual(shown, ['Copied.']);
  assert.equal(scheduledDelay, 2000);
  assert.equal(hidden, 1);

  scheduled();
  assert.equal(hidden, 2);
});

test('replaces an old feedback timer before showing a new message', () => {
  const cancelled = [];
  let nextTimer = 0;
  const feedback = createTransientFeedback({
    show: () => {},
    hide: () => {},
    schedule: () => {
      nextTimer += 1;
      return nextTimer;
    },
    cancel: (timer) => cancelled.push(timer),
  });

  feedback.show('Copied.');
  feedback.show('Copy failed.');

  assert.deepEqual(cancelled, [1]);
});
