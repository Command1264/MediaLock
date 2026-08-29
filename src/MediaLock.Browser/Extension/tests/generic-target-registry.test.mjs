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

test('page loading permanently clears the old live Endpoint and Page Binding', async () => {
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

  registry.clearTab(42);

  assert.equal(registry.get(42), undefined);
  assert.equal(registry.matches(first.target), false);
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

test('discarding a stale binding never clears its newer same-tab successor', async () => {
  let bindingIndex = 0;
  const bindings = ['old-binding', 'new-binding'];
  const authorization = {
    async authorizeTab({ scope, tab }) {
      const bindingId = bindings[bindingIndex++];
      return {
        accepted: true,
        binding: {
          bindingId,
          scope,
          tabId: tab.id,
          frameId: 0,
          documentId: `${bindingId}-document`,
          pageOrigin: 'https://media.example.test',
        },
      };
    },
    matches() {
      return true;
    },
    clearTab() {},
  };
  const tabs = {
    async sendMessage(_tabId, message) {
      return {
        accepted: true,
        endpointId: `${message.binding.bindingId}-endpoint`,
        capabilities: ['pause'],
      };
    },
  };
  const registry = createBrowserMediaTargetRegistry({ authorization, tabs });
  const tab = { id: 42, url: 'https://media.example.test/watch' };
  const oldBinding = await registry.bindTab({ scope: 'site', tab });
  const newBinding = await registry.bindTab({ scope: 'site', tab });

  assert.equal(registry.discard(oldBinding.target), false);
  assert.equal(registry.get(42).bindingId, 'new-binding');
  assert.equal(registry.discard(newBinding.target), true);
  assert.equal(registry.get(42), undefined);
});

test('an exact live presentation can update the command capability gate', async () => {
  const authorization = {
    async authorizeTab({ scope, tab }) {
      return {
        accepted: true,
        binding: {
          bindingId: 'binding-capability-update',
          scope,
          tabId: tab.id,
          frameId: 0,
          documentId: 'document-capability-update',
          pageOrigin: 'https://media.example.test',
        },
      };
    },
    matches() { return true; },
    clearTab() {},
  };
  const tabs = {
    async sendMessage() {
      return {
        accepted: true,
        endpointId: 'endpoint-capability-update',
        capabilities: ['play'],
      };
    },
  };
  const registry = createBrowserMediaTargetRegistry({ authorization, tabs });
  const bound = await registry.bindTab({
    scope: 'site',
    tab: { id: 42, url: 'https://media.example.test/watch' },
  });
  const stale = { ...bound.target, documentId: 'stale-document' };

  assert.equal(registry.supports(bound.target, 'seek'), false);
  assert.equal(registry.updateCapabilities(stale, ['play', 'seek']), false);
  assert.equal(registry.updateCapabilities(bound.target, ['play', 'seek']), true);
  assert.equal(registry.supports(bound.target, 'seek'), true);
});
