const OPAQUE_ENDPOINT_ID_PATTERN = /^[\x21-\x7e]{1,128}$/;
const GENERIC_CAPABILITIES = new Set(['pause', 'play', 'seek']);
const BINDING_ERRORS = new Set([
  'ambiguous-media-elements',
  'media-element-unavailable',
]);

export function createBrowserMediaTargetRegistry({ authorization, tabs }) {
  const targets = new Map();
  const matches = (target) => {
    const current = targets.get(target.tabId)?.target;
    return current !== undefined
      && authorization.matches(target)
      && current.bindingId === target.bindingId
      && current.endpointId === target.endpointId
      && current.scope === target.scope
      && current.frameId === target.frameId
      && current.documentId === target.documentId
      && current.pageOrigin === target.pageOrigin;
  };

  return Object.freeze({
    async bindActiveTemporaryTarget() {
      const authorized = await authorization.authorizeActivePage({ scope: 'temporary' });
      if (authorized.accepted !== true) {
        return authorized;
      }

      let endpoint;
      try {
        endpoint = await tabs.sendMessage(
          authorized.binding.tabId,
          { type: 'bindGenericEndpoint', binding: authorized.binding },
          { documentId: authorized.binding.documentId },
        );
      } catch {
        return { accepted: false, errorCode: 'target-unavailable' };
      }
      if (endpoint?.accepted !== true
          || typeof endpoint.endpointId !== 'string'
          || !OPAQUE_ENDPOINT_ID_PATTERN.test(endpoint.endpointId)
          || !Array.isArray(endpoint.capabilities)
          || endpoint.capabilities.length === 0
          || new Set(endpoint.capabilities).size !== endpoint.capabilities.length
          || endpoint.capabilities.some((capability) => !GENERIC_CAPABILITIES.has(capability))) {
        return {
          accepted: false,
          errorCode: BINDING_ERRORS.has(endpoint?.errorCode)
            ? endpoint.errorCode
            : 'target-unavailable',
        };
      }

      const target = Object.freeze({
        bindingId: authorized.binding.bindingId,
        endpointId: endpoint.endpointId,
        scope: authorized.binding.scope,
        tabId: authorized.binding.tabId,
        frameId: authorized.binding.frameId,
        documentId: authorized.binding.documentId,
        pageOrigin: authorized.binding.pageOrigin,
      });
      const capabilities = Object.freeze([...endpoint.capabilities]);
      targets.set(target.tabId, Object.freeze({ target, capabilities }));
      return {
        accepted: true,
        target,
        capabilities,
      };
    },

    get(tabId) {
      return targets.get(tabId)?.target;
    },

    matches,

    supports(target, commandName) {
      const current = targets.get(target.tabId);
      return current !== undefined
        && matches(target)
        && current.capabilities.includes(commandName);
    },
  });
}
