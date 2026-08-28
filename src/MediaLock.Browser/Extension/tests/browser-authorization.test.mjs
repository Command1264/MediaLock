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

test('clearing a tab invalidates an exact-site binding without revoking its site grant', async () => {
  const authorization = createBrowserAuthorizationModule({
    tabs: {
      async query() {
        return [{ id: 42, url: 'https://media.example.test/watch' }];
      },
    },
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId: 'document-cleared-site' }];
      },
    },
    permissions: {
      async contains() {
        return true;
      },
    },
    createBindingId: () => 'binding-cleared-site',
  });
  const result = await authorization.authorizeActivePage({ scope: 'site' });

  authorization.clearTab(42);

  assert.equal(authorization.matches(result.binding), false);
});

test('a trusted completed tab can receive a new exact-site Page Binding without becoming active', async () => {
  const authorization = createBrowserAuthorizationModule({
    tabs: {},
    scripting: {
      async executeScript(injection) {
        assert.deepEqual(injection.target, { tabId: 77, frameIds: [0] });
        return [{ frameId: 0, documentId: 'document-auto-site' }];
      },
    },
    permissions: {
      async contains(request) {
        assert.deepEqual(request, { origins: ['https://media.example.test/*'] });
        return true;
      },
    },
    createBindingId: () => 'binding-auto-site',
  });

  assert.deepEqual(await authorization.authorizeTab({
    scope: 'site',
    tab: { id: 77, url: 'https://media.example.test/new-document' },
  }), {
    accepted: true,
    binding: {
      bindingId: 'binding-auto-site',
      scope: 'site',
      tabId: 77,
      frameId: 0,
      documentId: 'document-auto-site',
      pageOrigin: 'https://media.example.test',
    },
  });
});
