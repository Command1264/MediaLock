export function createPendingRequestRegistry(capacity, timeoutMilliseconds, onTimeout = () => {}) {
  if (!Number.isSafeInteger(capacity) || capacity < 1 || capacity > 4096) {
    throw new TypeError('Pending request capacity must be an integer from 1 through 4096.');
  }
  if (!Number.isSafeInteger(timeoutMilliseconds)
      || timeoutMilliseconds < 1
      || timeoutMilliseconds > 10000) {
    throw new TypeError('Pending request timeout must be an integer from 1 through 10000.');
  }
  if (typeof onTimeout !== 'function') {
    throw new TypeError('Pending request timeout callback must be a function.');
  }

  const requests = new Map();
  return Object.freeze({
    get size() {
      return requests.size;
    },
    add(requestId) {
      if (requests.size >= capacity) {
        throw new Error('Pending request capacity was reached.');
      }
      if (requests.has(requestId)) {
        throw new Error('Pending request ID is already registered.');
      }

      return new Promise((resolve) => {
        const entry = { claimed: false, resolve, timeoutId: undefined };
        requests.set(requestId, entry);
        entry.timeoutId = setTimeout(() => {
          if (!requests.delete(requestId)) {
            return;
          }
          resolve({ accepted: false, errorCode: 'outcome-unknown' });
          onTimeout(requestId);
        }, timeoutMilliseconds);
      });
    },
    claim(requestId) {
      const entry = requests.get(requestId);
      if (!entry || entry.claimed) {
        return false;
      }
      entry.claimed = true;
      return true;
    },
    take(requestId) {
      const entry = requests.get(requestId);
      if (!entry) {
        return undefined;
      }
      requests.delete(requestId);
      clearTimeout(entry.timeoutId);
      return entry;
    },
    reset(outcome) {
      for (const [requestId, entry] of requests) {
        requests.delete(requestId);
        clearTimeout(entry.timeoutId);
        entry.resolve(outcome);
      }
    },
  });
}
