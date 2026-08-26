import test from 'node:test';
import assert from 'node:assert/strict';

import { createBrowserAuthorizationModule } from '../browser-authorization.mjs';
import { createBrowserMediaTargetRegistry } from '../generic-target-registry.mjs';

test('temporary authorization binds one Endpoint to the exact injected document', async () => {
  const calls = [];
  const tabs = {
    async query() {
      return [{ id: 42, url: 'https://media.example.test/watch' }];
    },
    async sendMessage(...args) {
      calls.push(args);
      return {
        accepted: true,
        endpointId: 'endpoint-0123456789abcdef',
        capabilities: ['pause'],
      };
    },
  };
  const authorization = createBrowserAuthorizationModule({
    tabs,
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId: 'document-0123456789abcdef' }];
      },
    },
    createBindingId: () => 'binding-0123456789abcdef',
  });
  const registry = createBrowserMediaTargetRegistry({ authorization, tabs });

  const result = await registry.bindActiveTemporaryTarget();

  assert.deepEqual(result, {
    accepted: true,
    target: {
      bindingId: 'binding-0123456789abcdef',
      endpointId: 'endpoint-0123456789abcdef',
      scope: 'temporary',
      tabId: 42,
      frameId: 0,
      documentId: 'document-0123456789abcdef',
      pageOrigin: 'https://media.example.test',
    },
    capabilities: ['pause'],
  });
  assert.equal(registry.matches(result.target), true);
  assert.equal(registry.supports(result.target, 'pause'), true);
  assert.equal(registry.supports(result.target, 'play'), false);
  assert.deepEqual(calls, [[
    42,
    {
      type: 'bindGenericEndpoint',
      binding: {
        bindingId: 'binding-0123456789abcdef',
        scope: 'temporary',
        tabId: 42,
        frameId: 0,
        documentId: 'document-0123456789abcdef',
        pageOrigin: 'https://media.example.test',
      },
    },
    { documentId: 'document-0123456789abcdef' },
  ]]);
});

test('an untrusted page error cannot cross the target registry', async () => {
  const tabs = {
    async query() {
      return [{ id: 42, url: 'https://media.example.test/watch' }];
    },
    async sendMessage() {
      return { accepted: false, errorCode: 'page-controlled-error-text' };
    },
  };
  const authorization = createBrowserAuthorizationModule({
    tabs,
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId: 'document-0123456789abcdef' }];
      },
    },
    createBindingId: () => 'binding-0123456789abcdef',
  });
  const registry = createBrowserMediaTargetRegistry({ authorization, tabs });

  assert.deepEqual(await registry.bindActiveTemporaryTarget(), {
    accepted: false,
    errorCode: 'target-unavailable',
  });
});
