import test from 'node:test';
import assert from 'node:assert/strict';

await import('../media-policy.js');
await import('../generic-media-adapter.js');
const { isSeekAllowed } = globalThis.MediaLockBrowserIntegration;
const { createGenericMediaAdapter } = globalThis.MediaLockGenericWeb;

test('one compatible media element binds and receives one Pause command', () => {
  let pauseCount = 0;
  const media = {
    paused: false,
    isConnected: true,
    pause() {
      pauseCount += 1;
      this.paused = true;
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

  assert.equal(binding.accepted, true);
  assert.equal(binding.endpointId, 'endpoint-0123456789abcdef');
  assert.deepEqual(binding.capabilities, ['pause', 'play']);
  assert.equal(binding.presentation.playbackStatus, 'playing');
  assert.equal(result.accepted, true);
  assert.equal(result.errorCode, null);
  assert.equal(result.presentation.playbackStatus, 'paused');
  assert.equal(pauseCount, 1);
});

test('one compatible media element binds and receives one Play command', async () => {
  let playCount = 0;
  const media = {
    isConnected: true,
    pause() {},
    async play() {
      playCount += 1;
    },
  };
  const adapter = createGenericMediaAdapter({
    getCandidates: () => [media],
    isMediaElement: (candidate) => candidate === media,
    createEndpointId: () => 'endpoint-play-0123456789',
  });

  const binding = adapter.bindSingleEndpoint();
  const result = await adapter.execute({
    endpointId: binding.endpointId,
    command: { name: 'play' },
  });

  assert.equal(binding.accepted, true);
  assert.equal(binding.endpointId, 'endpoint-play-0123456789');
  assert.deepEqual(binding.capabilities, ['pause', 'play']);
  assert.equal(binding.presentation.playbackStatus, 'playing');
  assert.equal(result.accepted, true);
  assert.equal(result.errorCode, null);
  assert.equal(result.presentation.playbackStatus, 'playing');
  assert.equal(playCount, 1);
});

test('a rejected Play command reports play-rejected without retrying', async () => {
  let playCount = 0;
  const media = {
    isConnected: true,
    pause() {},
    async play() {
      playCount += 1;
      throw new Error('Autoplay policy rejected playback.');
    },
  };
  const adapter = createGenericMediaAdapter({
    getCandidates: () => [media],
    isMediaElement: (candidate) => candidate === media,
    createEndpointId: () => 'endpoint-rejected-play',
  });

  const binding = adapter.bindSingleEndpoint();
  const result = await adapter.execute({
    endpointId: binding.endpointId,
    command: { name: 'play' },
  });

  assert.deepEqual(result, { accepted: false, errorCode: 'play-rejected' });
  assert.equal(playCount, 1);
});

test('one compatible media element receives one bounded Seek command', () => {
  let seekCount = 0;
  let currentTime = 0;
  const media = {
    isConnected: true,
    duration: 120,
    seekable: {
      length: 1,
      start: () => 0,
      end: () => 120,
    },
    pause() {},
    async play() {},
  };
  Object.defineProperty(media, 'currentTime', {
    get: () => currentTime,
    set(value) {
      seekCount += 1;
      currentTime = value;
    },
  });
  const adapter = createGenericMediaAdapter({
    getCandidates: () => [media],
    isMediaElement: (candidate) => candidate === media,
    createEndpointId: () => 'endpoint-seek-0123456789',
    isSeekAllowed,
  });

  const binding = adapter.bindSingleEndpoint();
  const result = adapter.execute({
    endpointId: binding.endpointId,
    command: { name: 'seek', positionSeconds: 30 },
  });

  assert.deepEqual(binding.capabilities, ['pause', 'play', 'seek']);
  assert.equal(result.accepted, true);
  assert.equal(result.errorCode, null);
  assert.equal(result.presentation.timeline.positionSeconds, 30);
  assert.equal(currentTime, 30);
  assert.equal(seekCount, 1);
});

test('an out-of-range Seek is rejected without moving the media element', () => {
  let currentTime = 10;
  let seekCount = 0;
  const media = {
    isConnected: true,
    duration: 120,
    seekable: {
      length: 1,
      start: () => 0,
      end: () => 120,
    },
    pause() {},
    async play() {},
  };
  Object.defineProperty(media, 'currentTime', {
    get: () => currentTime,
    set(value) {
      seekCount += 1;
      currentTime = value;
    },
  });
  const adapter = createGenericMediaAdapter({
    getCandidates: () => [media],
    isMediaElement: (candidate) => candidate === media,
    createEndpointId: () => 'endpoint-invalid-seek',
    isSeekAllowed,
  });
  const binding = adapter.bindSingleEndpoint();

  const result = adapter.execute({
    endpointId: binding.endpointId,
    command: { name: 'seek', positionSeconds: 121 },
  });

  assert.deepEqual(result, { accepted: false, errorCode: 'seek-out-of-range' });
  assert.equal(currentTime, 10);
  assert.equal(seekCount, 0);
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

test('a detached bound media element is unavailable instead of selecting a replacement', () => {
  let replacementPauseCount = 0;
  const original = { isConnected: true, pause() {}, async play() {} };
  const replacement = {
    isConnected: true,
    pause() {
      replacementPauseCount += 1;
    },
    async play() {},
  };
  let candidates = [original];
  const adapter = createGenericMediaAdapter({
    getCandidates: () => candidates,
    isMediaElement: (candidate) => candidate === original || candidate === replacement,
    createEndpointId: () => 'endpoint-original-media',
  });
  const binding = adapter.bindSingleEndpoint();
  original.isConnected = false;
  candidates = [replacement];

  const result = adapter.execute({
    endpointId: binding.endpointId,
    command: { name: 'pause' },
  });

  assert.deepEqual(result, { accepted: false, errorCode: 'media-element-unavailable' });
  assert.equal(replacementPauseCount, 0);
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
