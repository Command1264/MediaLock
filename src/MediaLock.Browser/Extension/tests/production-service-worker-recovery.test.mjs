import test from 'node:test';
import assert from 'node:assert/strict';

test('an authorized page retries Native Host discovery after Media Lock starts later', async () => {
  const listeners = {};
  const event = (name) => ({
    addListener(listener) { (listeners[name] ??= []).push(listener); },
  });
  let connectAttempts = 0;
  const scheduled = [];
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
        throw new Error('Native Host unavailable in deterministic test.');
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
  } finally {
    globalThis.setTimeout = originalSetTimeout;
    globalThis.clearTimeout = originalClearTimeout;
    delete globalThis.chrome;
  }
});
