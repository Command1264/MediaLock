const GENERIC_CONTENT_SCRIPT_FILES = Object.freeze([
  'media-policy.js',
  'generic-media-adapter.js',
  'generic-content-controller.js',
  'generic-media-content-script.js',
]);
const OPAQUE_DOCUMENT_ID_PATTERN = /^[\x21-\x7e]{1,256}$/;
const OPAQUE_BINDING_ID_PATTERN = /^[\x21-\x7e]{1,128}$/;

export function createBrowserAuthorizationModule({
  tabs,
  scripting,
  permissions,
  createBindingId = () => crypto.randomUUID(),
}) {
  const bindings = new Map();
  permissions?.onRemoved?.addListener((removed) => {
    const removedOrigins = new Set(Array.isArray(removed?.origins) ? removed.origins : []);
    for (const [tabId, binding] of bindings) {
      if (binding.scope === 'site'
          && removedOrigins.has(`${binding.pageOrigin}/*`)) {
        bindings.delete(tabId);
      }
    }
  });

  const authorizeTab = async ({ scope, tab }) => {
    if (scope !== 'temporary' && scope !== 'site') {
      throw new TypeError('Page authorization scope is invalid.');
    }
    if (!Number.isSafeInteger(tab?.id) || typeof tab?.url !== 'string') {
      return { accepted: false, errorCode: 'target-unavailable' };
    }

    let pageUrl;
    try {
      pageUrl = new URL(tab.url);
    } catch {
      return { accepted: false, errorCode: 'page-not-eligible' };
    }
    if (pageUrl.protocol !== 'https:') {
      return { accepted: false, errorCode: 'page-not-eligible' };
    }

    if (scope === 'site') {
      const granted = await permissions?.contains({ origins: [`${pageUrl.origin}/*`] });
      if (granted !== true) {
        return { accepted: false, errorCode: 'permission-denied' };
      }
    }

    const injectionResults = await scripting.executeScript({
      target: { tabId: tab.id, frameIds: [0] },
      files: [...GENERIC_CONTENT_SCRIPT_FILES],
    });
    if (!Array.isArray(injectionResults)
        || injectionResults.length !== 1
        || injectionResults[0].frameId !== 0
        || typeof injectionResults[0].documentId !== 'string'
        || !OPAQUE_DOCUMENT_ID_PATTERN.test(injectionResults[0].documentId)) {
      return { accepted: false, errorCode: 'document-identity-unavailable' };
    }

    const bindingId = createBindingId();
    if (typeof bindingId !== 'string' || !OPAQUE_BINDING_ID_PATTERN.test(bindingId)) {
      return { accepted: false, errorCode: 'binding-identity-unavailable' };
    }
    const binding = Object.freeze({
      bindingId,
      scope,
      tabId: tab.id,
      frameId: 0,
      documentId: injectionResults[0].documentId,
      pageOrigin: pageUrl.origin,
    });
    bindings.set(binding.tabId, binding);
    return { accepted: true, binding };
  };

  return Object.freeze({
    async authorizeActivePage({ scope }) {
      if (scope !== 'temporary' && scope !== 'site') {
        throw new TypeError('Page authorization scope is invalid.');
      }

      const activeTabs = await tabs.query({ active: true, currentWindow: true });
      if (activeTabs.length !== 1) {
        return { accepted: false, errorCode: 'target-unavailable' };
      }
      return authorizeTab({ scope, tab: activeTabs[0] });
    },

    authorizeTab,

    clearTab(tabId) {
      bindings.delete(tabId);
    },

    matches(binding) {
      const current = bindings.get(binding.tabId);
      return current !== undefined
        && current.bindingId === binding.bindingId
        && current.scope === binding.scope
        && current.frameId === binding.frameId
        && current.documentId === binding.documentId
        && current.pageOrigin === binding.pageOrigin;
    },
  });
}
