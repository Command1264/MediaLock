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

  return Object.freeze({
    async authorizeActivePage({ scope }) {
      if (scope !== 'temporary' && scope !== 'site') {
        throw new TypeError('Page authorization scope is invalid.');
      }

      const activeTabs = await tabs.query({ active: true, currentWindow: true });
      if (activeTabs.length !== 1
          || !Number.isSafeInteger(activeTabs[0].id)
          || typeof activeTabs[0].url !== 'string') {
        return { accepted: false, errorCode: 'target-unavailable' };
      }

      let pageUrl;
      try {
        pageUrl = new URL(activeTabs[0].url);
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
        target: { tabId: activeTabs[0].id, frameIds: [0] },
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
        tabId: activeTabs[0].id,
        frameId: 0,
        documentId: injectionResults[0].documentId,
        pageOrigin: pageUrl.origin,
      });
      bindings.set(binding.tabId, binding);
      return { accepted: true, binding };
    },

    async rebindTab(tabId) {
      const current = bindings.get(tabId);
      if (current?.scope !== 'site') {
        return { accepted: false, errorCode: 'target-unavailable' };
      }

      let tab;
      let pageUrl;
      try {
        tab = await tabs.get(tabId);
        pageUrl = new URL(tab.url);
      } catch {
        return { accepted: false, errorCode: 'target-unavailable' };
      }
      if (pageUrl.protocol !== 'https:' || pageUrl.origin !== current.pageOrigin) {
        return { accepted: false, errorCode: 'target-unavailable' };
      }
      const originPattern = `${current.pageOrigin}/*`;
      if (await permissions?.contains({ origins: [originPattern] }) !== true) {
        bindings.delete(tabId);
        return { accepted: false, errorCode: 'permission-denied' };
      }

      let injectionResults;
      try {
        injectionResults = await scripting.executeScript({
          target: { tabId, frameIds: [0] },
          files: [...GENERIC_CONTENT_SCRIPT_FILES],
        });
      } catch {
        return { accepted: false, errorCode: 'target-unavailable' };
      }
      if (!Array.isArray(injectionResults)
          || injectionResults.length !== 1
          || injectionResults[0].frameId !== 0
          || typeof injectionResults[0].documentId !== 'string'
          || !OPAQUE_DOCUMENT_ID_PATTERN.test(injectionResults[0].documentId)) {
        return { accepted: false, errorCode: 'document-identity-unavailable' };
      }

      const binding = Object.freeze({
        ...current,
        documentId: injectionResults[0].documentId,
      });
      bindings.set(tabId, binding);
      return { accepted: true, binding };
    },

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
