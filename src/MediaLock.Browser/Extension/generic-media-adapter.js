(() => {
  function createGenericMediaAdapter({
    getCandidates,
    isMediaElement,
    createEndpointId,
    isSeekAllowed,
    getSourceDisplayName = () => 'Authorized web media',
  }) {
    let boundEndpoint;
    let detachObservation = () => {};

    const currentCapabilities = () => {
      const capabilities = ['pause', 'play', 'toggle'];
      if (typeof isSeekAllowed === 'function'
          && Number.isFinite(boundEndpoint.media.duration)
          && Number.isSafeInteger(boundEndpoint.media.seekable?.length)
          && boundEndpoint.media.seekable.length > 0) {
        capabilities.push('seek');
      }
      return Object.freeze(capabilities);
    };

    const createPresentation = () => {
      const timeline = Number.isFinite(boundEndpoint.media.duration)
        && boundEndpoint.media.duration > 0
        && Number.isFinite(boundEndpoint.media.currentTime)
        ? Object.freeze({
          startSeconds: 0,
          endSeconds: boundEndpoint.media.duration,
          positionSeconds: Math.max(0, Math.min(
            boundEndpoint.media.currentTime,
            boundEndpoint.media.duration,
          )),
        })
        : null;
      return Object.freeze({
        sourceDisplayName: String(getSourceDisplayName()).slice(0, 256)
          || 'Authorized web media',
        playbackStatus: boundEndpoint.media.paused ? 'paused' : 'playing',
        playbackRate: normalizePlaybackRate(boundEndpoint.media.playbackRate),
        capabilities: currentCapabilities(),
        observedAt: new Date().toISOString(),
        timeline,
      });
    };

    return Object.freeze({
      bindSingleEndpoint(onPresentationChanged) {
        const candidates = [...getCandidates()].filter(isMediaElement);
        if (candidates.length > 1) {
          return {
            accepted: false,
            errorCode: 'ambiguous-media-elements',
          };
        }
        if (candidates.length === 0) {
          return {
            accepted: false,
            errorCode: 'media-element-unavailable',
          };
        }

        detachObservation();
        boundEndpoint = Object.freeze({
          endpointId: createEndpointId(),
          media: candidates[0],
        });
        detachObservation = observeMedia(
          boundEndpoint,
          createPresentation,
          onPresentationChanged,
        );
        return Object.freeze({
          accepted: true,
          endpointId: boundEndpoint.endpointId,
          capabilities: currentCapabilities(),
          presentation: createPresentation(),
        });
      },

      execute({ endpointId, command }) {
        if (!boundEndpoint
            || endpointId !== boundEndpoint.endpointId
            || boundEndpoint.media.isConnected !== true
            || !currentCapabilities().includes(command?.name)) {
          return { accepted: false, errorCode: 'media-element-unavailable' };
        }

        if (command.name === 'pause'
            || (command.name === 'toggle' && boundEndpoint.media.paused !== true)) {
          boundEndpoint.media.pause();
          return {
            accepted: true,
            errorCode: null,
            presentation: createPresentation(),
          };
        }

        if (command.name === 'seek') {
          if (!isSeekAllowed(
            command.positionSeconds,
            boundEndpoint.media.duration,
            boundEndpoint.media.seekable,
          )) {
            return { accepted: false, errorCode: 'seek-out-of-range' };
          }
          boundEndpoint.media.currentTime = command.positionSeconds;
          return {
            accepted: true,
            errorCode: null,
            presentation: createPresentation(),
          };
        }

        return Promise.resolve()
          .then(() => boundEndpoint.media.play())
          .then(
            () => ({
              accepted: true,
              errorCode: null,
              presentation: createPresentation(),
            }),
            () => ({ accepted: false, errorCode: 'play-rejected' }),
          );
      },
    });
  }

  function normalizePlaybackRate(value) {
    return Number.isFinite(value) && value >= 0 && value <= 16 ? value : 1;
  }

  function observeMedia(endpoint, createPresentation, onPresentationChanged) {
    if (typeof onPresentationChanged !== 'function'
        || typeof endpoint.media.addEventListener !== 'function'
        || typeof endpoint.media.removeEventListener !== 'function') {
      return () => {};
    }
    let immediatePending = false;
    let timelineTimer;
    const publish = () => {
      if (endpoint.media.isConnected !== true) {
        return;
      }
      onPresentationChanged(createPresentation());
    };
    const publishImmediate = () => {
      if (immediatePending) {
        return;
      }
      immediatePending = true;
      queueMicrotask(() => {
        immediatePending = false;
        publish();
      });
    };
    const publishTimeline = () => {
      if (timelineTimer !== undefined) {
        return;
      }
      timelineTimer = setTimeout(() => {
        timelineTimer = undefined;
        publish();
      }, 1000);
    };
    const immediateEvents = [
      'play',
      'pause',
      'ratechange',
      'durationchange',
      'loadedmetadata',
      'emptied',
      'ended',
    ];
    for (const eventName of immediateEvents) {
      endpoint.media.addEventListener(eventName, publishImmediate);
    }
    endpoint.media.addEventListener('timeupdate', publishTimeline);
    endpoint.media.addEventListener('progress', publishTimeline);
    return () => {
      for (const eventName of immediateEvents) {
        endpoint.media.removeEventListener(eventName, publishImmediate);
      }
      endpoint.media.removeEventListener('timeupdate', publishTimeline);
      endpoint.media.removeEventListener('progress', publishTimeline);
      if (timelineTimer !== undefined) {
        clearTimeout(timelineTimer);
      }
    };
  }

  globalThis.MediaLockGenericWeb = Object.freeze({ createGenericMediaAdapter });
})();
