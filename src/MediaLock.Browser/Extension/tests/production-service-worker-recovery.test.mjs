import test from 'node:test';
import assert from 'node:assert/strict';
import { deriveConnectionId } from '../production-protocol.mjs';

test('an authorized page publishes once and stops recovery after Media Lock starts later', async () => {
  const listeners = {};
  const event = (name) => ({
    addListener(listener) { (listeners[name] ??= []).push(listener); },
  });
  let connectAttempts = 0;
  const nativeMessages = [];
  const nativeListeners = {};
  const nativeEvent = (name) => ({
    addListener(listener) { (nativeListeners[name] ??= []).push(listener); },
  });
  const nativePort = {
    onMessage: nativeEvent('message'),
    onDisconnect: nativeEvent('disconnect'),
    disconnect() {},
    postMessage(message) {
      nativeMessages.push(message);
      if (message.type === 'extensionHello') {
        void deriveConnectionId(message).then((connectionId) => {
          for (const listener of nativeListeners.message ?? []) {
            listener({
              protocolVersion: 2,
              type: 'helloAck',
              hostNonce: message.hostNonce,
              extensionNonce: message.extensionNonce,
              connectionId,
              browserFamily: message.browserFamily,
              profileId: message.profileId,
              capabilities: message.capabilities,
            });
          }
        });
      }
    },
  };
  const scheduled = [];
  const tabMessages = [];
  const originalSetTimeout = globalThis.setTimeout;
  const originalClearTimeout = globalThis.clearTimeout;
  globalThis.setTimeout = (callback, delay) => {
    const handle = { callback, delay };
    scheduled.push(handle);
    return handle;
  };
  globalThis.clearTimeout = (handle) => { handle.cancelled = true; };
  globalThis.chrome = {
    alarms: {
      onAlarm: event('alarm'),
      create() {},
      async clear() { return true; },
    },
    tabs: {
      onUpdated: event('tabUpdated'),
      onRemoved: event('tabRemoved'),
      async query() {
        return [{
          id: 42,
          status: 'complete',
          url: 'https://media.example.test/watch',
        }];
      },
      async sendMessage(_tabId, message) {
        tabMessages.push(message);
        if (message.type === 'genericCommand') {
          return { accepted: true };
        }
        assert.equal(message.type, 'bindGenericEndpoint');
        return {
          accepted: true,
          endpointId: 'endpoint-start-order',
          capabilities: ['play'],
          presentation: {
            sourceDisplayName: 'Media',
            playbackStatus: 'playing',
            playbackRate: 1,
            capabilities: ['play'],
            observedAt: new Date().toISOString(),
            timeline: null,
          },
        };
      },
    },
    permissions: {
      onAdded: event('permissionAdded'),
      onRemoved: event('permissionRemoved'),
      async contains() { return true; },
    },
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId: 'document-start-order' }];
      },
    },
    runtime: {
      id: 'kggfkkiifnclhhmibdglkbdfbacakemn',
      onMessage: event('runtimeMessage'),
      connectNative() {
        connectAttempts += 1;
        if (connectAttempts === 1) {
          throw new Error('Native Host unavailable in deterministic test.');
        }
        queueMicrotask(() => {
          for (const listener of nativeListeners.message ?? []) {
            listener({
              protocolVersion: 2,
              type: 'hostHello',
              hostNonce: '11111111-1111-4111-8111-111111111111',
              capabilities: ['pause', 'play', 'seek', 'toggle'],
            });
          }
        });
        return nativePort;
      },
    },
    storage: {
      local: {
        async get() { return { profileId: '22222222-2222-4222-8222-222222222222' }; },
        async set() {},
      },
    },
  };

  try {
    await import(`../production-service-worker.mjs?test=recovery-${Date.now()}`);
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(connectAttempts, 1);

    const alarmListener = listeners.alarm?.at(-1);
    assert.equal(typeof alarmListener, 'function');
    await alarmListener({ name: 'media-lock-native-recovery' });
    await new Promise((resolve) => setImmediate(resolve));

    assert.equal(connectAttempts, 2);
    assert.equal(
      nativeMessages.filter((message) => message.type === 'targetSnapshot').length,
      1,
    );

    const initialSnapshot = nativeMessages.find((message) => message.type === 'targetSnapshot');
    const runtimeMessageListener = listeners.runtimeMessage?.at(-1);
    assert.equal(typeof runtimeMessageListener, 'function');
    const presentationResponse = await new Promise((resolve) => {
      assert.equal(runtimeMessageListener({
        type: 'genericPresentationChanged',
        target: initialSnapshot.target,
        presentation: {
          sourceDisplayName: 'Media',
          playbackStatus: 'playing',
          playbackRate: 1,
          capabilities: ['play', 'seek'],
          observedAt: new Date().toISOString(),
          timeline: {
            startSeconds: 0,
            endSeconds: 300,
            positionSeconds: 30,
          },
        },
      }, {
        id: chrome.runtime.id,
        tab: { id: initialSnapshot.target.tabId },
        frameId: 0,
        documentId: initialSnapshot.target.documentId,
      }, resolve), true);
    });
    assert.deepEqual(presentationResponse, { accepted: true });
    const updatedSnapshots = nativeMessages.filter((message) => message.type === 'targetSnapshot');
    assert.equal(updatedSnapshots.length, 2);
    assert.deepEqual(updatedSnapshots.at(-1).presentation.capabilities, ['play', 'seek']);

    for (const listener of nativeListeners.message ?? []) {
      listener({
        protocolVersion: 2,
        type: 'command',
        connectionId: updatedSnapshots.at(-1).connectionId,
        sequence: 1,
        requestId: '33333333-3333-4333-8333-333333333333',
        target: initialSnapshot.target,
        command: { name: 'seek', positionSeconds: 90 },
      });
    }
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(
      tabMessages.filter((message) => message.type === 'genericCommand').map((message) => message.command),
      [{ name: 'seek', positionSeconds: 90 }],
    );
    assert.equal(
      nativeMessages.filter((message) => message.type === 'commandResult').at(-1)?.accepted,
      true,
    );

    await alarmListener({ name: 'media-lock-native-recovery' });
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(connectAttempts, 2);
    assert.equal(
      nativeMessages.filter((message) => message.type === 'targetSnapshot').length,
      2,
    );
  } finally {
    globalThis.setTimeout = originalSetTimeout;
    globalThis.clearTimeout = originalClearTimeout;
    delete globalThis.chrome;
  }
});
