import { copyStatusText, createTransientFeedback } from './status-copy.mjs';

const status = document.querySelector('#status');
const copyStatusButton = document.querySelector('#copy-status');
const copyFeedback = document.querySelector('#copy-feedback');
const transientCopyFeedback = createTransientFeedback({
  show: (text) => {
    copyFeedback.textContent = text;
    copyFeedback.hidden = false;
  },
  hide: () => {
    copyFeedback.textContent = '';
    copyFeedback.hidden = true;
  },
});

document.querySelectorAll('[data-command]').forEach((button) => {
  button.addEventListener('click', () => runCommand({ name: button.dataset.command }));
});

document.querySelector('#seek').addEventListener('click', () => {
  const positionSeconds = Number(document.querySelector('#seek-seconds').value);
  if (!Number.isFinite(positionSeconds) || positionSeconds < 0) {
    setStatus('Enter a finite, non-negative position.');
    return;
  }
  runCommand({ name: 'seek', positionSeconds });
});

copyStatusButton.addEventListener('click', async () => {
  setControlsDisabled(true);
  try {
    await copyStatusText(status.textContent, (text) => navigator.clipboard.writeText(text));
    showCopyFeedback('Copied.');
  } catch {
    showCopyFeedback('Copy failed.');
  } finally {
    setControlsDisabled(false);
  }
});

async function runCommand(command) {
  setControlsDisabled(true);
  setStatus('Sending one command through the Native Host…');
  try {
    const result = await chrome.runtime.sendMessage({ type: 'probeCommand', command });
    setStatus(result?.accepted === true
      ? 'Accepted once by the active media element.'
      : `Rejected: ${result?.errorCode ?? 'unknown-error'}`);
  } catch {
    setStatus('Rejected: extension-connection-failed');
  } finally {
    setControlsDisabled(false);
  }
}

function setStatus(text) {
  status.textContent = text;
  clearCopyFeedback();
}

function showCopyFeedback(text) {
  transientCopyFeedback.show(text);
}

function clearCopyFeedback() {
  transientCopyFeedback.clear();
}

function setControlsDisabled(disabled) {
  document.querySelectorAll('button, input').forEach((control) => {
    control.disabled = disabled;
  });
}
