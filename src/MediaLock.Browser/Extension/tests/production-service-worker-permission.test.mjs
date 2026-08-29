import test from 'node:test';
import assert from 'node:assert/strict';

test('the production Service Worker continues a first site grant after Popup replacement', async () => {
  let permissionAdded;
  const event = (capture) => ({ addListener(listener) { capture(listener); } });
  globalThis.chrome = {
    alarms: {
      onAlarm: event(() => {}),
      create() {},
      async clear() { return true; },
    },
    tabs: {
      onUpdated: event(() => {}),
      onRemoved: event(() => {}),
      async query() { return []; },
    },
    permissions: {
      onAdded: event((listener) => { permissionAdded = listener; }),
      onRemoved: event(() => {}),
      async contains() { return true; },
    },
    runtime: {
      id: 'kggfkkiifnclhhmibdglkbdfbacakemn',
      onMessage: event(() => {}),
    },
    scripting: {},
  };

  try {
    await import(`../production-service-worker.mjs?test=permission-${Date.now()}`);
    assert.equal(typeof permissionAdded, 'function');
  } finally {
    delete globalThis.chrome;
  }
});
