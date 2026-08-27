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
        capabilities: boundEndpoint.capabilities,
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

        const capabilities = ['pause', 'play'];
        if (typeof isSeekAllowed === 'function'
            && Number.isFinite(candidates[0].duration)
            && Number.isSafeInteger(candidates[0].seekable?.length)
            && candidates[0].seekable.length > 0) {
          capabilities.push('seek');
        }
        detachObservation();
        boundEndpoint = Object.freeze({
          endpointId: createEndpointId(),
          media: candidates[0],
          capabilities: Object.freeze(capabilities),
        });
        detachObservation = observeMedia(
          boundEndpoint,
          createPresentation,
          onPresentationChanged,
        );
        return Object.freeze({
          accepted: true,
          endpointId: boundEndpoint.endpointId,
          capabilities: boundEndpoint.capabilities,
          presentation: createPresentation(),
        });
      },

      execute({ endpointId, command }) {
        if (!boundEndpoint
            || endpointId !== boundEndpoint.endpointId
            || boundEndpoint.media.isConnected !== true
            || !boundEndpoint.capabilities.includes(command?.name)) {
          return { accepted: false, errorCode: 'media-element-unavailable' };
        }

        if (command.name === 'pause') {
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
    return () => {
      for (const eventName of immediateEvents) {
        endpoint.media.removeEventListener(eventName, publishImmediate);
      }
      endpoint.media.removeEventListener('timeupdate', publishTimeline);
      if (timelineTimer !== undefined) {
        clearTimeout(timelineTimer);
      }
    };
  }

  globalThis.MediaLockGenericWeb = Object.freeze({ createGenericMediaAdapter });
})();
