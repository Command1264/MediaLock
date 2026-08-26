(() => {
  const allowedOrigins = new Set([
    'https://www.youtube.com',
    'https://music.youtube.com',
  ]);
  const allowedCommands = new Set(['play', 'pause', 'seek']);

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (sender.id !== chrome.runtime.id
        || !sender.url?.startsWith(`chrome-extension://${chrome.runtime.id}/`)
        || window.top !== window
        || !allowedOrigins.has(window.location.origin)
        || message?.type !== 'command'
        || message?.target?.frameId !== 0
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
      if (!Number.isFinite(position) || position < 0 || position > media.duration) {
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
