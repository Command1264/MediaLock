const ALLOWED_PAGE_ORIGINS = new Set([
  'https://www.youtube.com',
  'https://music.youtube.com',
]);
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function createDocumentRegistry(extensionId) {
  if (typeof extensionId !== 'string' || extensionId.length === 0) {
    throw new TypeError('Extension ID is required.');
  }

  const bindings = new Map();
  return Object.freeze({
    register(sender) {
      if (sender?.id !== extensionId || !Number.isSafeInteger(sender?.tab?.id)) {
        throw new Error('Document sender is not authorized.');
      }
      if (sender.frameId !== 0) {
        throw new Error('Only the top frame can register a document.');
      }
      if (sender.documentLifecycle !== undefined && sender.documentLifecycle !== 'active') {
        throw new Error('Only an active document can be registered.');
      }
      if (typeof sender.documentId !== 'string' || !UUID_PATTERN.test(sender.documentId)) {
        throw new Error('Browser document ID must be a UUID.');
      }
      if (!ALLOWED_PAGE_ORIGINS.has(sender.origin)) {
        throw new Error('Document origin is not authorized.');
      }
      if (typeof sender.url !== 'string' || new URL(sender.url).origin !== sender.origin) {
        throw new Error('Document URL does not match its origin.');
      }

      const binding = Object.freeze({
        tabId: sender.tab.id,
        frameId: 0,
        documentId: sender.documentId,
        pageOrigin: sender.origin,
      });
      bindings.set(binding.tabId, binding);
      return binding;
    },
    get(tabId) {
      return bindings.get(tabId);
    },
    matches(binding) {
      const current = bindings.get(binding.tabId);
      return current !== undefined
        && current.documentId === binding.documentId
        && current.frameId === binding.frameId
        && current.pageOrigin === binding.pageOrigin;
    },
    clear(tabId) {
      bindings.delete(tabId);
    },
  });
}
