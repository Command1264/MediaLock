const ALLOWED_PAGE_ORIGINS = new Set([
  'https://www.youtube.com',
  'https://music.youtube.com',
]);
const OPAQUE_DOCUMENT_ID_PATTERN = /^[\x21-\x7e]{1,256}$/;

class DocumentRegistrationError extends Error {
  constructor(code, message) {
    super(message);
    this.code = code;
  }
}

export function createDocumentRegistry(extensionId) {
  if (typeof extensionId !== 'string' || extensionId.length === 0) {
    throw new TypeError('Extension ID is required.');
  }

  const bindings = new Map();
  return Object.freeze({
    register(sender, currentTab) {
      if (sender?.id !== extensionId || !Number.isSafeInteger(sender?.tab?.id)) {
        throw new DocumentRegistrationError(
          'registration-sender-rejected',
          'Document sender is not authorized.',
        );
      }
      if (currentTab?.id !== sender.tab.id) {
        throw new DocumentRegistrationError(
          'registration-tab-identity-rejected',
          'Document tab identity does not match the sender.',
        );
      }
      if (sender.frameId !== 0) {
        throw new DocumentRegistrationError(
          'registration-frame-rejected',
          'Only the top frame can register a document.',
        );
      }
      if (sender.documentLifecycle !== undefined && sender.documentLifecycle !== 'active') {
        throw new DocumentRegistrationError(
          'registration-lifecycle-rejected',
          'Only an active document can be registered.',
        );
      }
      if (typeof sender.documentId !== 'string'
        || !OPAQUE_DOCUMENT_ID_PATTERN.test(sender.documentId)) {
        throw new DocumentRegistrationError(
          'registration-document-id-rejected',
          'Browser document ID must be a bounded opaque identifier.',
        );
      }
      if (!ALLOWED_PAGE_ORIGINS.has(sender.origin)) {
        throw new DocumentRegistrationError(
          'registration-origin-rejected',
          'Document origin is not authorized.',
        );
      }
      if (typeof sender.url !== 'string' || new URL(sender.url).origin !== sender.origin) {
        throw new DocumentRegistrationError(
          'registration-sender-url-rejected',
          'Document URL does not match its origin.',
        );
      }
      if (typeof currentTab.url !== 'string'
          || new URL(currentTab.url).origin !== sender.origin) {
        throw new DocumentRegistrationError(
          'registration-tab-url-rejected',
          'Document tab URL does not match its origin.',
        );
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
