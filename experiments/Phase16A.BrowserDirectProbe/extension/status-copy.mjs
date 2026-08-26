export async function copyStatusText(statusText, writeText) {
  if (typeof statusText !== 'string' || statusText.trim().length === 0) {
    throw new Error('A non-empty status is required.');
  }
  if (typeof writeText !== 'function') {
    throw new Error('A clipboard writer is required.');
  }

  await writeText(statusText);
}

export function createTransientFeedback({
  show,
  hide,
  schedule = setTimeout,
  cancel = clearTimeout,
  durationMilliseconds = 2000,
}) {
  let timer;

  function clear() {
    if (timer !== undefined) {
      cancel(timer);
      timer = undefined;
    }
    hide();
  }

  return {
    clear,
    show(text) {
      clear();
      show(text);
      timer = schedule(() => {
        timer = undefined;
        hide();
      }, durationMilliseconds);
    },
  };
}
