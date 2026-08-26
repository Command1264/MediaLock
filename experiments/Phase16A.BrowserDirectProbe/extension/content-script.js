(() => {
  const allowedOrigins = new Set([
    'https://www.youtube.com',
    'https://music.youtube.com',
  ]);
  const allowedCommands = new Set(['play', 'pause', 'seek']);
  let registeredDocumentId;

  function registerDocument() {
    return chrome.runtime.sendMessage({ type: 'registerDocument' }).then((result) => {
      registeredDocumentId = result?.accepted === true ? result.documentId : undefined;
      return result;
    });
  }

  void registerDocument();

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message?.type === 'requestDocumentRegistration'
        && sender.id === chrome.runtime.id
        && sender.url?.startsWith(`chrome-extension://${chrome.runtime.id}/`)) {
      registerDocument().then(sendResponse, () => {
        sendResponse({ accepted: false, errorCode: 'unauthorized-command' });
      });
      return true;
    }

    if (sender.id !== chrome.runtime.id
        || !sender.url?.startsWith(`chrome-extension://${chrome.runtime.id}/`)
        || window.top !== window
        || !allowedOrigins.has(window.location.origin)
        || message?.type !== 'command'
        || message?.target?.frameId !== 0
        || message?.target?.documentId !== registeredDocumentId
        || message?.target?.pageOrigin !== window.location.origin
        || !allowedCommands.has(message?.command?.name)) {
      sendResponse({ accepted: false, errorCode: 'unauthorized-command' });
      return false;
    }

    const media = document.querySelector('video, audio');
    if (!(media instanceof HTMLMediaElement)) {
      sendResponse({ accepted: false, errorCode: 'media-element-unavailable' });
      return false;
    }

    if (message.command.name === 'pause') {
      media.pause();
      sendResponse({ accepted: true, errorCode: null });
      return false;
    }

    if (message.command.name === 'seek') {
      const position = message.command.positionSeconds;
      if (!globalThis.MediaLockPhase16A?.isSeekAllowed(
        position,
        media.duration,
        media.seekable,
      )) {
        sendResponse({ accepted: false, errorCode: 'seek-out-of-range' });
        return false;
      }
      media.currentTime = position;
      sendResponse({ accepted: true, errorCode: null });
      return false;
    }

    media.play().then(
      () => sendResponse({ accepted: true, errorCode: null }),
      () => sendResponse({ accepted: false, errorCode: 'play-rejected' }),
    );
    return true;
  });
})();
