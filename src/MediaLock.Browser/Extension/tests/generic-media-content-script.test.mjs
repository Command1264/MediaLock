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

test('an external Pause publishes one exact-target presentation update', async () => {
  const runtimeListeners = [];
  const mediaListeners = new Map();
  const sentMessages = [];
  class FakeMediaElement {}
  const media = new FakeMediaElement();
  Object.assign(media, {
    isConnected: true,
    paused: false,
    playbackRate: 1.5,
    duration: 180,
    currentTime: 45,
    seekable: { length: 1, start: () => 0, end: () => 180 },
    pause() {},
    async play() {},
    addEventListener(name, listener) {
      mediaListeners.set(name, listener);
    },
    removeEventListener(name) {
      mediaListeners.delete(name);
    },
  });
  const extensionId = 'abcdefghijklmnopabcdefghijklmnop';
  const context = vm.createContext({
    HTMLMediaElement: FakeMediaElement,
    document: {
      title: 'External Pause Video',
      querySelectorAll: () => [media],
    },
    window: { location: { origin: 'https://media.example.test' } },
    crypto: { randomUUID: () => 'endpoint-external-pause' },
    console,
    setTimeout,
    clearTimeout,
    queueMicrotask,
    chrome: {
      runtime: {
        id: extensionId,
        onMessage: {
          addListener(listener) {
            runtimeListeners.push(listener);
          },
        },
        async sendMessage(message) {
          sentMessages.push(message);
        },
      },
    },
  });
  context.globalThis = context;
  for (const source of sourceFiles) {
    vm.runInContext(source, context);
  }
  const sender = {
    id: extensionId,
    url: `chrome-extension://${extensionId}/service-worker.mjs`,
  };
  const binding = {
    bindingId: 'binding-external-pause',
    scope: 'temporary',
    tabId: 42,
    frameId: 0,
    documentId: 'document-external-pause',
    pageOrigin: 'https://media.example.test',
  };
  let endpoint;
  runtimeListeners[0]({ type: 'bindGenericEndpoint', binding }, sender, (response) => {
    endpoint = response;
  });

  media.paused = true;
  media.currentTime = 46;
  mediaListeners.get('pause')();
  await new Promise((resolve) => setImmediate(resolve));

  assert.equal(endpoint.accepted, true);
  assert.equal(sentMessages.length, 1);
  assert.equal(sentMessages[0].type, 'genericPresentationChanged');
  assert.deepEqual(
    JSON.parse(JSON.stringify(sentMessages[0].target)),
    { ...binding, endpointId: endpoint.endpointId },
  );
  assert.equal(sentMessages[0].presentation.playbackStatus, 'paused');
  assert.equal(sentMessages[0].presentation.playbackRate, 1.5);
  assert.equal(sentMessages[0].presentation.timeline.positionSeconds, 46);
});

test('an invalidated Extension context does not leak from a stale media listener', async () => {
  const runtimeListeners = [];
  const mediaListeners = new Map();
  const debugMessages = [];
  class FakeMediaElement {}
  const media = new FakeMediaElement();
  Object.assign(media, {
    isConnected: true,
    paused: false,
    playbackRate: 1,
    duration: 180,
    currentTime: 45,
    seekable: { length: 1, start: () => 0, end: () => 180 },
    pause() {},
    async play() {},
    addEventListener(name, listener) {
      mediaListeners.set(name, listener);
    },
    removeEventListener(name) {
      mediaListeners.delete(name);
    },
  });
  const extensionId = 'abcdefghijklmnopabcdefghijklmnop';
  const context = vm.createContext({
    HTMLMediaElement: FakeMediaElement,
    document: {
      title: 'Reloaded Extension Video',
      querySelectorAll: () => [media],
    },
    window: { location: { origin: 'https://media.example.test' } },
    crypto: { randomUUID: () => 'endpoint-invalidated-context' },
    console: {
      debug(...args) {
        debugMessages.push(args);
      },
    },
    setTimeout,
    clearTimeout,
    queueMicrotask,
    chrome: {
      runtime: {
        id: extensionId,
        onMessage: {
          addListener(listener) {
            runtimeListeners.push(listener);
          },
        },
        sendMessage() {
          throw new Error('Extension context invalidated.');
        },
      },
    },
  });
  context.globalThis = context;
  for (const source of sourceFiles) {
    vm.runInContext(source, context);
  }
  const sender = {
    id: extensionId,
    url: `chrome-extension://${extensionId}/service-worker.mjs`,
  };
  const binding = {
    bindingId: 'binding-invalidated-context',
    scope: 'temporary',
    tabId: 42,
    frameId: 0,
    documentId: 'document-invalidated-context',
    pageOrigin: 'https://media.example.test',
  };
  runtimeListeners[0]({ type: 'bindGenericEndpoint', binding }, sender, () => {});

  media.paused = true;
  mediaListeners.get('pause')();
  await new Promise((resolve) => setImmediate(resolve));

  assert.equal(debugMessages.length, 1);
  assert.equal(debugMessages[0][0], 'Media Lock presentation update was not delivered.');
});

test('the content message boundary delivers exact unbind and clears Popup authorization', () => {
  const listeners = [];
  class FakeMediaElement {}
  const media = new FakeMediaElement();
  Object.assign(media, {
    isConnected: true,
    pause() {},
    async play() {},
  });
  const extensionId = 'abcdefghijklmnopabcdefghijklmnop';
  const context = vm.createContext({
    HTMLMediaElement: FakeMediaElement,
    document: { title: 'Revoked Video', querySelectorAll: () => [media] },
    window: { location: { origin: 'https://media.example.test' } },
    crypto: { randomUUID: () => 'endpoint-runtime-revoke' },
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
  const sender = {
    id: extensionId,
    url: `chrome-extension://${extensionId}/service-worker.mjs`,
  };
  const binding = {
    bindingId: 'binding-runtime-revoke',
    scope: 'temporary',
    tabId: 42,
    frameId: 0,
    documentId: 'document-runtime-revoke',
    pageOrigin: 'https://media.example.test',
  };
  let endpoint;
  listeners[0]({ type: 'bindGenericEndpoint', binding }, sender, (response) => {
    endpoint = response;
  });
  const target = { ...binding, endpointId: endpoint.endpointId };
  const responses = [];

  listeners[0]({ type: 'unbindGenericEndpoint', target }, sender, (response) => {
    responses.push(response);
  });
  listeners[0]({ type: 'getGenericEndpointStatus' }, sender, (response) => {
    responses.push(response);
  });

  assert.deepEqual(JSON.parse(JSON.stringify(responses)), [
    { accepted: true },
    { authorized: false },
  ]);
});
