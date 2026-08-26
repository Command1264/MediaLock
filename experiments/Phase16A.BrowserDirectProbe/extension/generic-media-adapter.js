(() => {
  function createGenericMediaAdapter({
    getCandidates,
    isMediaElement,
    createEndpointId,
    isSeekAllowed,
  }) {
    let boundEndpoint;

    return Object.freeze({
      bindSingleEndpoint() {
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
        boundEndpoint = Object.freeze({
          endpointId: createEndpointId(),
          media: candidates[0],
          capabilities: Object.freeze(capabilities),
        });
        return Object.freeze({
          accepted: true,
          endpointId: boundEndpoint.endpointId,
          capabilities: boundEndpoint.capabilities,
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
          return { accepted: true, errorCode: null };
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
          return { accepted: true, errorCode: null };
        }

        return Promise.resolve()
          .then(() => boundEndpoint.media.play())
          .then(
            () => ({ accepted: true, errorCode: null }),
            () => ({ accepted: false, errorCode: 'play-rejected' }),
          );
      },
    });
  }

  globalThis.MediaLockGenericWeb = Object.freeze({ createGenericMediaAdapter });
})();
