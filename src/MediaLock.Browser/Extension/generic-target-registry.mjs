const OPAQUE_ENDPOINT_ID_PATTERN = /^[\x21-\x7e]{1,128}$/;
const GENERIC_CAPABILITIES = new Set(['pause', 'play', 'seek', 'toggle']);
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

  const bindAuthorized = async (authorized) => {
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
    const presentation = endpoint.presentation !== null
      && typeof endpoint.presentation === 'object'
      ? endpoint.presentation
      : Object.freeze({
        sourceDisplayName: 'Authorized web media',
        playbackStatus: 'unknown',
        playbackRate: 1,
        capabilities,
        observedAt: new Date().toISOString(),
        timeline: null,
      });
    targets.set(target.tabId, Object.freeze({ target, capabilities }));
    return {
      accepted: true,
      target,
      capabilities,
      presentation,
    };
  };

  return Object.freeze({
    async bindActiveTarget({ scope }) {
      const authorized = await authorization.authorizeActivePage({ scope });
      return bindAuthorized(authorized);
    },

    async bindTab({ scope, tab }) {
      const authorized = await authorization.authorizeTab({ scope, tab });
      return bindAuthorized(authorized);
    },

    clearTab(tabId) {
      targets.delete(tabId);
      authorization.clearTab(tabId);
    },

    discard(target) {
      const current = targets.get(target?.tabId)?.target;
      if (current === undefined
          || current.bindingId !== target?.bindingId
          || current.endpointId !== target?.endpointId
          || current.scope !== target?.scope
          || current.frameId !== target?.frameId
          || current.documentId !== target?.documentId
          || current.pageOrigin !== target?.pageOrigin) {
        return false;
      }
      targets.delete(target.tabId);
      authorization.clearTab(target.tabId);
      return true;
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

    updateCapabilities(target, capabilities) {
      const current = targets.get(target?.tabId);
      if (current === undefined
          || !matches(target)
          || !Array.isArray(capabilities)
          || capabilities.length === 0
          || new Set(capabilities).size !== capabilities.length
          || capabilities.some((capability) => !GENERIC_CAPABILITIES.has(capability))) {
        return false;
      }
      targets.set(target.tabId, Object.freeze({
        target: current.target,
        capabilities: Object.freeze([...capabilities]),
      }));
      return true;
    },
  });
}
