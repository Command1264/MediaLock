import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const extensionRoot = new URL('../', import.meta.url);
const fixedContentScript = await readFile(new URL('content-script.js', extensionRoot), 'utf8');

test('the fixed-site listener ignores Generic Adapter messages without responding', async () => {
  const listeners = [];
  const extensionId = 'abcdefghijklmnopabcdefghijklmnop';
  const context = vm.createContext({
    document: { querySelector: () => null },
    window: {
      top: null,
      location: { origin: 'https://www.youtube.com' },
    },
    chrome: {
      runtime: {
        id: extensionId,
        sendMessage: async () => ({ accepted: true, documentId: 'fixed-document' }),
        onMessage: {
          addListener(listener) {
            listeners.push(listener);
          },
        },
      },
    },
  });
  context.window.top = context.window;
  context.globalThis = context;
  vm.runInContext(fixedContentScript, context);
  await new Promise((resolve) => setImmediate(resolve));

  const responses = [];
  const keepsChannelOpen = listeners[0](
    { type: 'bindGenericEndpoint' },
    {
      id: extensionId,
      url: `chrome-extension://${extensionId}/service-worker.mjs`,
    },
    (response) => responses.push(response),
  );

  assert.equal(keepsChannelOpen, false);
  assert.deepEqual(responses, []);
});
