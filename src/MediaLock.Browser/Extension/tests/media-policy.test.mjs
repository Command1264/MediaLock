import test from 'node:test';
import assert from 'node:assert/strict';

await import('../media-policy.js');
const { isSeekAllowed } = globalThis.MediaLockBrowserIntegration;

function ranges(values) {
  return {
    length: values.length,
    start: (index) => values[index][0],
    end: (index) => values[index][1],
  };
}

test('accepts a finite position only inside one finite seekable range', () => {
  assert.equal(isSeekAllowed(80, 200, ranges([[0, 120], [150, 200]])), true);
  assert.equal(isSeekAllowed(140, 200, ranges([[0, 120], [150, 200]])), false);
});

test('rejects live, unknown, empty, malformed, and out-of-duration seeks', () => {
  assert.equal(isSeekAllowed(80, Number.POSITIVE_INFINITY, ranges([[0, 120]])), false);
  assert.equal(isSeekAllowed(80, Number.NaN, ranges([[0, 120]])), false);
  assert.equal(isSeekAllowed(80, 200, ranges([])), false);
  assert.equal(isSeekAllowed(80, 200, ranges([[0, Number.POSITIVE_INFINITY]])), false);
  assert.equal(isSeekAllowed(201, 200, ranges([[0, 300]])), false);
  assert.equal(isSeekAllowed(-1, 200, ranges([[0, 200]])), false);
});
