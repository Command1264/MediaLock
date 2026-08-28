import test from 'node:test';
import assert from 'node:assert/strict';
import { handleNativePortDisconnect } from '../native-connection-lifecycle.mjs';

test('native disconnect consumes Chromium lastError and does not disconnect the closed port again', () => {
  let lastErrorReads = 0;
  let disconnectPort;
  const runtime = {};
  Object.defineProperty(runtime, 'lastError', {
    get() {
      lastErrorReads += 1;
      return { message: 'Error when communicating with the native messaging host.' };
    },
  });

  const message = handleNativePortDisconnect(runtime, (options) => {
    disconnectPort = options.disconnectPort;
  });

  assert.equal(message, 'Error when communicating with the native messaging host.');
  assert.equal(lastErrorReads, 1);
  assert.equal(disconnectPort, false);
});
