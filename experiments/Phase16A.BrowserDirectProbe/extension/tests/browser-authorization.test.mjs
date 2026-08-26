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
