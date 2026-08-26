import test from 'node:test';
import assert from 'node:assert/strict';

await import('../generic-media-adapter.js');
const { createGenericMediaAdapter } = globalThis.MediaLockGenericWeb;

test('one compatible media element binds and receives one Pause command', () => {
  let pauseCount = 0;
  const media = {
    isConnected: true,
    pause() {
      pauseCount += 1;
    },
    play: async () => {},
  };
  const adapter = createGenericMediaAdapter({
    getCandidates: () => [media],
    isMediaElement: (candidate) => candidate === media,
    createEndpointId: () => 'endpoint-0123456789abcdef',
  });

  const binding = adapter.bindSingleEndpoint();
  const result = adapter.execute({
    endpointId: binding.endpointId,
    command: { name: 'pause' },
  });

  assert.deepEqual(binding, {
    accepted: true,
    endpointId: 'endpoint-0123456789abcdef',
    capabilities: ['pause'],
  });
  assert.deepEqual(result, { accepted: true, errorCode: null });
  assert.equal(pauseCount, 1);
});

test('multiple compatible media elements are ambiguous instead of list-order selected', () => {
  const media = [
    { isConnected: true, pause() {} },
    { isConnected: true, pause() {} },
  ];
  const adapter = createGenericMediaAdapter({
    getCandidates: () => media,
    isMediaElement: (candidate) => media.includes(candidate),
    createEndpointId: () => 'must-not-be-issued',
  });

  assert.deepEqual(adapter.bindSingleEndpoint(), {
    accepted: false,
    errorCode: 'ambiguous-media-elements',
  });
});

test('a page without compatible media is unavailable without throwing', () => {
  const adapter = createGenericMediaAdapter({
    getCandidates: () => [],
    isMediaElement: () => false,
    createEndpointId: () => 'must-not-be-issued',
  });

  assert.deepEqual(adapter.bindSingleEndpoint(), {
    accepted: false,
    errorCode: 'media-element-unavailable',
  });
});
