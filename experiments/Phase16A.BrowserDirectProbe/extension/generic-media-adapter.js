(() => {
  function createGenericMediaAdapter({
    getCandidates,
    isMediaElement,
    createEndpointId,
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

        boundEndpoint = Object.freeze({
          endpointId: createEndpointId(),
          media: candidates[0],
        });
        return Object.freeze({
          accepted: true,
          endpointId: boundEndpoint.endpointId,
          capabilities: Object.freeze(['pause', 'play']),
        });
      },

      execute({ endpointId, command }) {
        if (!boundEndpoint
            || endpointId !== boundEndpoint.endpointId
            || boundEndpoint.media.isConnected !== true
            || (command?.name !== 'pause' && command?.name !== 'play')) {
          return { accepted: false, errorCode: 'media-element-unavailable' };
        }

        if (command.name === 'pause') {
          boundEndpoint.media.pause();
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
