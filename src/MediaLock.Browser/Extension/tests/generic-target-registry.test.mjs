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

  const result = await registry.bindActiveTarget({ scope: 'temporary' });

  assert.deepEqual({
    accepted: result.accepted,
    target: result.target,
    capabilities: result.capabilities,
  }, {
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

  assert.deepEqual(await registry.bindActiveTarget({ scope: 'temporary' }), {
    accepted: false,
    errorCode: 'target-unavailable',
  });
});

test('an allowed site reload replaces only its live Endpoint', async () => {
  let documentId = 'document-before-reload';
  const tabs = {
    async query() {
      return [{ id: 42, url: 'https://media.example.test/watch' }];
    },
    async get() {
      return { id: 42, url: 'https://media.example.test/watch-next' };
    },
    async sendMessage(tabId, message) {
      return {
        accepted: true,
        endpointId: `endpoint-for-${message.binding.documentId}`,
        capabilities: ['pause', 'play', 'seek'],
      };
    },
  };
  const authorization = createBrowserAuthorizationModule({
    tabs,
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId }];
      },
    },
    permissions: {
      async request() {
        return true;
      },
      async contains() {
        return true;
      },
    },
    createBindingId: () => 'binding-site-reload',
  });
  const registry = createBrowserMediaTargetRegistry({ authorization, tabs });
  const first = await registry.bindActiveTarget({ scope: 'site' });
  documentId = 'document-after-reload';

  const successor = await registry.rebindTab(42);

  assert.equal(successor.accepted, true);
  assert.equal(successor.target.bindingId, first.target.bindingId);
  assert.notEqual(successor.target.endpointId, first.target.endpointId);
  assert.equal(registry.matches(first.target), false);
  assert.equal(registry.matches(successor.target), true);
});

test('page loading immediately suspends the old live Endpoint', async () => {
  const tabs = {
    async query() {
      return [{ id: 42, url: 'https://media.example.test/watch' }];
    },
    async sendMessage() {
      return {
        accepted: true,
        endpointId: 'endpoint-before-loading',
        capabilities: ['pause'],
      };
    },
  };
  const authorization = createBrowserAuthorizationModule({
    tabs,
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId: 'document-before-loading' }];
      },
    },
    createBindingId: () => 'binding-before-loading',
  });
  const registry = createBrowserMediaTargetRegistry({ authorization, tabs });
  const first = await registry.bindActiveTarget({ scope: 'temporary' });

  registry.suspendTab(42);

  assert.equal(registry.get(42), undefined);
  assert.equal(registry.matches(first.target), false);

  registry.clearTab(42);

  assert.equal(authorization.matches(first.target), false);
});

test('two same-origin pages with identical presentation remain distinct targets', async () => {
  const activeTabs = [
    { id: 41, url: 'https://media.example.test/watch', title: 'Same title' },
    { id: 42, url: 'https://media.example.test/watch', title: 'Same title' },
  ];
  let callIndex = 0;
  const tabs = {
    async query() {
      return [activeTabs[callIndex]];
    },
    async sendMessage(tabId) {
      return {
        accepted: true,
        endpointId: `endpoint-tab-${tabId}`,
        capabilities: ['pause'],
      };
    },
  };
  const authorization = createBrowserAuthorizationModule({
    tabs,
    scripting: {
      async executeScript() {
        return [{ frameId: 0, documentId: `document-${activeTabs[callIndex].id}` }];
      },
    },
    createBindingId: () => `binding-${activeTabs[callIndex++].id}`,
  });
  const registry = createBrowserMediaTargetRegistry({ authorization, tabs });

  const first = await registry.bindActiveTarget({ scope: 'temporary' });
  const second = await registry.bindActiveTarget({ scope: 'temporary' });

  assert.notEqual(first.target.bindingId, second.target.bindingId);
  assert.notEqual(first.target.endpointId, second.target.endpointId);
  assert.equal(registry.get(41).bindingId, first.target.bindingId);
  assert.equal(registry.get(42).bindingId, second.target.bindingId);
});
