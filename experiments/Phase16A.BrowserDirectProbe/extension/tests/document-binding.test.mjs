import test from 'node:test';
import assert from 'node:assert/strict';

import { createDocumentRegistry } from '../document-binding.mjs';

const extensionId = 'abcdefghijklmnopabcdefghijklmnop';
const documentId = '33333333-3333-4333-8333-333333333333';

function sender(overrides = {}) {
  const value = {
    id: extensionId,
    tab: { id: 42, url: 'https://music.youtube.com/watch?v=example' },
    frameId: 0,
    documentId,
    documentLifecycle: 'active',
    origin: 'https://music.youtube.com',
    url: 'https://music.youtube.com/watch?v=example',
    ...overrides,
  };
  return value;
}

test('registers only browser-owned active top-frame document metadata', () => {
  const registry = createDocumentRegistry(extensionId);

  const binding = registry.register(sender());

  assert.deepEqual(binding, {
    tabId: 42,
    frameId: 0,
    documentId,
    pageOrigin: 'https://music.youtube.com',
  });
  assert.deepEqual(registry.get(42), binding);
});

test('rejects an unauthorized, inactive, nested, or malformed document sender', () => {
  const registry = createDocumentRegistry(extensionId);

  assert.throws(() => registry.register(sender({ id: 'ponmlkjihgfedcbaponmlkjihgfedcba' })), /sender/i);
  assert.throws(() => registry.register(sender({ frameId: 1 })), /frame/i);
  assert.throws(() => registry.register(sender({ documentLifecycle: 'cached' })), /active/i);
  assert.throws(() => registry.register(sender({ documentId: 'page-chosen-value' })), /document/i);
  assert.throws(() => registry.register(sender({ origin: 'https://example.com' })), /origin/i);
  assert.throws(() => registry.register(sender({ tab: { id: 42 } })), /tab URL/i);
  assert.throws(() => registry.register(sender({
    tab: { id: 42, url: 'https://www.youtube.com/watch?v=other' },
  })), /tab URL/i);
});

test('replaces an old binding and refuses stale document matches', () => {
  const registry = createDocumentRegistry(extensionId);
  const oldBinding = registry.register(sender());
  const newDocumentId = '44444444-4444-4444-8444-444444444444';

  registry.clear(42);
  assert.equal(registry.matches(oldBinding), false);

  const newBinding = registry.register(sender({ documentId: newDocumentId }));
  assert.equal(registry.matches(oldBinding), false);
  assert.equal(registry.matches(newBinding), true);
});
