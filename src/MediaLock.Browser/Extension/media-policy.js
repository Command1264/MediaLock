(() => {
  function isSeekAllowed(position, duration, seekable) {
    if (!Number.isFinite(position)
        || position < 0
        || !Number.isFinite(duration)
        || duration < 0
        || position > duration
        || !Number.isSafeInteger(seekable?.length)
        || seekable.length < 1) {
      return false;
    }

    for (let index = 0; index < seekable.length; index += 1) {
      let start;
      let end;
      try {
        start = seekable.start(index);
        end = seekable.end(index);
      } catch {
        return false;
      }
      if (!Number.isFinite(start) || !Number.isFinite(end) || start < 0 || end < start) {
        return false;
      }
      if (position >= start && position <= end) {
        return true;
      }
    }
    return false;
  }

  globalThis.MediaLockBrowserIntegration = Object.freeze({ isSeekAllowed });
})();
