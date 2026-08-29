const RECOVERY_ALARM_NAME = 'media-lock-native-recovery';
const DEFAULT_RETRY_DELAYS_MILLISECONDS = Object.freeze([
  1000,
  2000,
  5000,
  10000,
  30000,
]);
const ALARM_DELAY_MINUTES = 0.5;

export function createNativeConnectionRecovery({
  alarms,
  attempt,
  reportFailure,
  schedule = setTimeout,
  cancelSchedule = clearTimeout,
  retryDelaysMilliseconds = DEFAULT_RETRY_DELAYS_MILLISECONDS,
}) {
  if (typeof alarms?.create !== 'function'
      || typeof alarms?.clear !== 'function'
      || typeof attempt !== 'function'
      || typeof reportFailure !== 'function'
      || typeof schedule !== 'function'
      || typeof cancelSchedule !== 'function'
      || !Array.isArray(retryDelaysMilliseconds)
      || retryDelaysMilliseconds.length === 0
      || retryDelaysMilliseconds.some((delay) => !Number.isSafeInteger(delay) || delay <= 0)) {
    throw new TypeError('Native connection recovery dependencies are invalid.');
  }

  let retryIndex = 0;
  let timer;
  let running;
  let requestedWhileRunning = false;

  const clearAlarm = () => {
    Promise.resolve(alarms.clear(RECOVERY_ALARM_NAME)).catch(reportFailure);
  };
  const cancelTimer = () => {
    if (timer !== undefined) {
      cancelSchedule(timer);
      timer = undefined;
    }
  };
  const stop = () => {
    cancelTimer();
    retryIndex = 0;
    clearAlarm();
  };
  const scheduleNext = () => {
    if (timer !== undefined) {
      return;
    }
    const delay = retryDelaysMilliseconds[
      Math.min(retryIndex, retryDelaysMilliseconds.length - 1)
    ];
    retryIndex = Math.min(
      retryIndex + 1,
      retryDelaysMilliseconds.length - 1,
    );
    timer = schedule(async () => {
      timer = undefined;
      await run();
    }, delay);
    try {
      alarms.create(RECOVERY_ALARM_NAME, { delayInMinutes: ALARM_DELAY_MINUTES });
    } catch (error) {
      reportFailure(error);
    }
  };
  const run = async () => {
    if (running !== undefined) {
      return running;
    }
    running = Promise.resolve()
      .then(attempt)
      .then(
        (shouldRetry) => {
          const retryRequested = shouldRetry === true || requestedWhileRunning;
          requestedWhileRunning = false;
          if (retryRequested) {
            scheduleNext();
          } else {
            stop();
          }
        },
        (error) => {
          requestedWhileRunning = false;
          reportFailure(error);
          scheduleNext();
        },
      )
      .finally(() => { running = undefined; });
    return running;
  };

  return Object.freeze({
    request() {
      if (running !== undefined) {
        requestedWhileRunning = true;
        return;
      }
      scheduleNext();
    },

    start() {
      return run();
    },

    handleAlarm(alarm) {
      if (alarm?.name !== RECOVERY_ALARM_NAME) {
        return Promise.resolve(false);
      }
      cancelTimer();
      return run().then(() => true);
    },

    connected: stop,
  });
}
