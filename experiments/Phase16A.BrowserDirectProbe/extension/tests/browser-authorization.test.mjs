import test from 'node:test';
import assert from 'node:assert/strict';

import { createBrowserAuthorizationModule } from '../browser-authorization.mjs';

test('a user gesture temporarily authorizes one active top-level HTTPS page', async () => {
  const calls = [];
  const authorization = createBrowserAuthorizationModule({
    tabs: {
      async query(query) {
        calls.push(['query', query]);
        return [{
          id: 42,
          url: 'https://media.example.test/watch/private-token?value=secret',
          title: 'Untrusted page title',
        }];
      },
    },
    scripting: {
      async executeScript(injection) {
        calls.push(['executeScript', injection]);
        return [{ frameId: 0, documentId: 'document-0123456789abcdef' }];
      },
    },
    createBindingId: () => 'binding-0123456789abcdef',
  });

  const result = await authorization.authorizeActivePage({ scope: 'temporary' });

  assert.deepEqual(result, {
    accepted: true,
    binding: {
      bindingId: 'binding-0123456789abcdef',
      scope: 'temporary',
      tabId: 42,
      frameId: 0,
      documentId: 'document-0123456789abcdef',
      pageOrigin: 'https://media.example.test',
    },
  });
  assert.equal(authorization.matches(result.binding), true);
  assert.deepEqual(calls, [
    ['query', { active: true, currentWindow: true }],
    ['executeScript', {
      target: { tabId: 42, frameIds: [0] },
      files: [
        'media-policy.js',
        'generic-media-adapter.js',
        'generic-content-controller.js',
        'generic-media-content-script.js',
      ],
    }],
  ]);
});

test('an invalid or non-HTTPS active page is ineligible before injection', async () => {
  let injectionCount = 0;
  const authorization = createBrowserAuthorizationModule({
    tabs: {
      async query() {
        return [{ id: 42, url: 'not a browser URL' }];
      },
    },
    scripting: {
      async executeScript() {
        injectionCount += 1;
        return [];
      },
    },
    createBindingId: () => 'must-not-be-issued',
  });

  assert.deepEqual(await authorization.authorizeActivePage({ scope: 'temporary' }), {
    accepted: false,
    errorCode: 'page-not-eligible',
  });
  assert.equal(injectionCount, 0);
});

test('a malformed Extension-issued Page Binding ID is rejected', async () => {
  const authorization = createBrowserAuthorizationModule({
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
    },
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId: 'document-0123456789abcdef' }];
      },
    },
    createBindingId: () => 'line\nbreak',
  });

  assert.deepEqual(await authorization.authorizeActivePage({ scope: 'temporary' }), {
    accepted: false,
    errorCode: 'binding-identity-unavailable',
  });
});

test('a user gesture grants one exact HTTPS site before binding its active page', async () => {
  const permissionChecks = [];
  const authorization = createBrowserAuthorizationModule({
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch?private=token' }];
      },
    },
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId: 'document-site-0123456789' }];
      },
    },
    permissions: {
      async contains(request) {
        permissionChecks.push(request);
        return true;
      },
    },
    createBindingId: () => 'binding-site-0123456789',
  });

  const result = await authorization.authorizeActivePage({ scope: 'site' });

  assert.deepEqual(permissionChecks, [{ origins: ['https://media.example.test/*'] }]);
  assert.deepEqual(result, {
    accepted: true,
    binding: {
      bindingId: 'binding-site-0123456789',
      scope: 'site',
      tabId: 42,
      frameId: 0,
      documentId: 'document-site-0123456789',
      pageOrigin: 'https://media.example.test',
    },
  });
});

test('a denied exact-site request does not inject or create a Page Binding', async () => {
  let injectionCount = 0;
  const authorization = createBrowserAuthorizationModule({
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
    },
    scripting: {
      async executeScript() {
        injectionCount += 1;
        return [];
      },
    },
    permissions: {
      async contains() {
        return false;
      },
    },
    createBindingId: () => 'must-not-be-issued',
  });

  assert.deepEqual(await authorization.authorizeActivePage({ scope: 'site' }), {
    accepted: false,
    errorCode: 'permission-denied',
  });
  assert.equal(injectionCount, 0);
});

test('revoking an exact site permission immediately invalidates its Page Binding', async () => {
  let permissionRemoved;
  const authorization = createBrowserAuthorizationModule({
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
    },
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId: 'document-revoked-site' }];
      },
    },
    permissions: {
      async contains() {
        return true;
      },
      onRemoved: {
        addListener(listener) {
          permissionRemoved = listener;
        },
      },
    },
    createBindingId: () => 'binding-revoked-site',
  });
  const result = await authorization.authorizeActivePage({ scope: 'site' });
  assert.equal(authorization.matches(result.binding), true);

  permissionRemoved({ origins: ['https://media.example.test/*'] });

  assert.equal(authorization.matches(result.binding), false);
});

test('an allowed site reload keeps its Page Binding and adopts the new browser document', async () => {
  let documentId = 'document-site-before-reload';
  const authorization = createBrowserAuthorizationModule({
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
      async get() {
        return { id: 42, url: 'https://media.example.test/next' };
      },
    },
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId }];
      },
    },
    permissions: {
      async contains() {
        return true;
      },
    },
    createBindingId: () => 'binding-site-reload',
  });
  const first = await authorization.authorizeActivePage({ scope: 'site' });
  documentId = 'document-site-after-reload';

  const successor = await authorization.rebindTab(42);

  assert.deepEqual(successor, {
    accepted: true,
    binding: {
      bindingId: 'binding-site-reload',
      scope: 'site',
      tabId: 42,
      frameId: 0,
      documentId: 'document-site-after-reload',
      pageOrigin: 'https://media.example.test',
    },
  });
  assert.equal(first.binding.bindingId, successor.binding.bindingId);
  assert.equal(authorization.matches(first.binding), false);
  assert.equal(authorization.matches(successor.binding), true);
});

test('cross-origin navigation suspends a site binding without injecting the destination', async () => {
  let injectionCount = 0;
  const authorization = createBrowserAuthorizationModule({
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
      async get() {
        return { id: 42, url: 'https://other.example.test/watch' };
      },
    },
    scripting: {
      async executeScript() {
        injectionCount += 1;
        return [{ frameId: 0, documentId: 'document-original-site' }];
      },
    },
    permissions: {
      async contains() {
        return true;
      },
    },
    createBindingId: () => 'binding-cross-origin',
  });
  await authorization.authorizeActivePage({ scope: 'site' });
  assert.equal(injectionCount, 1);

  const result = await authorization.rebindTab(42);

  assert.deepEqual(result, { accepted: false, errorCode: 'target-unavailable' });
  assert.equal(injectionCount, 1);
});
