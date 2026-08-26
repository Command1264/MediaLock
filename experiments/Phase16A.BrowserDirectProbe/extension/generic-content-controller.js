(() => {
  function createGenericContentController({ adapter, extensionId, pageOrigin }) {
    const extensionOrigin = `chrome-extension://${extensionId}/`;
    let activeTarget;

    return Object.freeze({
      handle(message, sender) {
        if (sender?.id !== extensionId || !sender?.url?.startsWith(extensionOrigin)) {
          return { accepted: false, errorCode: 'unauthorized-command' };
        }

        if (message?.type === 'bindGenericEndpoint') {
          const binding = message.binding;
          if (binding?.scope !== 'temporary'
              || binding?.frameId !== 0
              || binding?.pageOrigin !== pageOrigin
              || typeof binding?.bindingId !== 'string'
              || typeof binding?.documentId !== 'string'
              || !Number.isSafeInteger(binding?.tabId)) {
            return { accepted: false, errorCode: 'unauthorized-command' };
          }

          const endpoint = adapter.bindSingleEndpoint();
          if (endpoint.accepted !== true) {
            return endpoint;
          }
          activeTarget = Object.freeze({
            ...binding,
            endpointId: endpoint.endpointId,
          });
          return endpoint;
        }

        if (message?.type !== 'genericCommand'
            || !activeTarget
            || !sameTarget(message.target, activeTarget)) {
          return { accepted: false, errorCode: 'unauthorized-command' };
        }
        return adapter.execute({
          endpointId: activeTarget.endpointId,
          command: message.command,
        });
      },
    });
  }

  function sameTarget(candidate, expected) {
    return candidate?.bindingId === expected.bindingId
      && candidate?.scope === expected.scope
      && candidate?.tabId === expected.tabId
      && candidate?.frameId === expected.frameId
      && candidate?.documentId === expected.documentId
      && candidate?.pageOrigin === expected.pageOrigin
      && candidate?.endpointId === expected.endpointId;
  }

  globalThis.MediaLockGenericContent = Object.freeze({ createGenericContentController });
})();

