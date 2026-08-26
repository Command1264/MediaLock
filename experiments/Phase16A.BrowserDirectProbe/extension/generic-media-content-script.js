(() => {
  if (globalThis.MediaLockGenericContentRuntime?.installed === true) {
    return;
  }

  const adapter = globalThis.MediaLockGenericWeb.createGenericMediaAdapter({
    getCandidates: () => document.querySelectorAll('video, audio'),
    isMediaElement: (candidate) => candidate instanceof HTMLMediaElement,
    createEndpointId: () => crypto.randomUUID(),
  });
  const controller = globalThis.MediaLockGenericContent.createGenericContentController({
    adapter,
    extensionId: chrome.runtime.id,
    pageOrigin: window.location.origin,
  });

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.type !== 'bindGenericEndpoint' && message?.type !== 'genericCommand') {
      return false;
    }
    try {
      const result = controller.handle(message, sender);
      if (typeof result?.then === 'function') {
        result.then(
          sendResponse,
          () => sendResponse({ accepted: false, errorCode: 'media-element-unavailable' }),
        );
        return true;
      }
      sendResponse(result);
    } catch {
      sendResponse({ accepted: false, errorCode: 'media-element-unavailable' });
    }
    return false;
  });

  globalThis.MediaLockGenericContentRuntime = Object.freeze({ installed: true });
})();
