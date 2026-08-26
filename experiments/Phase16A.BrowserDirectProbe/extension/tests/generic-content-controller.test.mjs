import test from 'node:test';
import assert from 'node:assert/strict';

await import('../generic-media-adapter.js');
await import('../generic-content-controller.js');

const { createGenericMediaAdapter } = globalThis.MediaLockGenericWeb;
const { createGenericContentController } = globalThis.MediaLockGenericContent;

test('an exact Page Binding binds one Endpoint and dispatches Pause once', () => {
  let pauseCount = 0;
  const media = {
    isConnected: true,
    pause() {
      pauseCount += 1;
    },
  };
  const adapter = createGenericMediaAdapter({
    getCandidates: () => [media],
    isMediaElement: (candidate) => candidate === media,
    createEndpointId: () => 'endpoint-0123456789abcdef',
  });
  const controller = createGenericContentController({
    adapter,
    extensionId: 'abcdefghijklmnopabcdefghijklmnop',
    pageOrigin: 'https://media.example.test',
  });
  const sender = {
    id: 'abcdefghijklmnopabcdefghijklmnop',
    url: 'chrome-extension://abcdefghijklmnopabcdefghijklmnop/service-worker.mjs',
  };
  const binding = {
    bindingId: 'binding-0123456789abcdef',
    scope: 'temporary',
    tabId: 42,
    frameId: 0,
    documentId: 'document-0123456789abcdef',
    pageOrigin: 'https://media.example.test',
  };

  const endpoint = controller.handle({ type: 'bindGenericEndpoint', binding }, sender);
  const result = controller.handle({
    type: 'genericCommand',
    target: { ...binding, endpointId: endpoint.endpointId },
    command: { name: 'pause' },
  }, sender);

  assert.deepEqual(endpoint, {
    accepted: true,
    endpointId: 'endpoint-0123456789abcdef',
    capabilities: ['pause'],
  });
  assert.deepEqual(result, { accepted: true, errorCode: null });
  assert.equal(pauseCount, 1);
});
