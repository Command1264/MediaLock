import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const extensionRoot = new URL('../', import.meta.url);
const sourceFiles = await Promise.all([
  'generic-media-adapter.js',
  'generic-content-controller.js',
  'generic-media-content-script.js',
].map((name) => readFile(new URL(name, extensionRoot), 'utf8')));

test('the content message boundary awaits one Play result before responding', async () => {
  let playCount = 0;
  const listeners = [];
  class FakeMediaElement {}
  const media = new FakeMediaElement();
  media.isConnected = true;
  media.pause = () => {};
  media.play = async () => {
    playCount += 1;
  };
  const extensionId = 'abcdefghijklmnopabcdefghijklmnop';
  const context = vm.createContext({
    HTMLMediaElement: FakeMediaElement,
    document: { querySelectorAll: () => [media] },
    window: { location: { origin: 'https://media.example.test' } },
    crypto: { randomUUID: () => 'endpoint-runtime-play' },
    chrome: {
      runtime: {
        id: extensionId,
        onMessage: {
          addListener(listener) {
            listeners.push(listener);
          },
        },
      },
    },
  });
  context.globalThis = context;
  for (const source of sourceFiles) {
    vm.runInContext(source, context);
  }
  assert.equal(listeners.length, 1);

  const sender = {
    id: extensionId,
    url: `chrome-extension://${extensionId}/service-worker.mjs`,
  };
  const binding = {
    bindingId: 'binding-runtime-play',
    scope: 'temporary',
    tabId: 42,
    frameId: 0,
    documentId: 'document-runtime-play',
    pageOrigin: 'https://media.example.test',
  };
  let endpoint;
  listeners[0]({ type: 'bindGenericEndpoint', binding }, sender, (response) => {
    endpoint = response;
  });

  const responses = [];
  const keepsChannelOpen = listeners[0]({
    type: 'genericCommand',
    target: { ...binding, endpointId: endpoint.endpointId },
    command: { name: 'play' },
  }, sender, (response) => {
    responses.push(response);
  });
  await new Promise((resolve) => setImmediate(resolve));

  assert.equal(keepsChannelOpen, true);
  assert.equal(responses.length, 1);
  assert.equal(responses[0].accepted, true);
  assert.equal(responses[0].errorCode, null);
  assert.equal(playCount, 1);
});
