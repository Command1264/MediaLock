const status = document.querySelector('#status');

document.querySelectorAll('[data-command]').forEach((button) => {
  button.addEventListener('click', () => runCommand({ name: button.dataset.command }));
});

document.querySelector('#seek').addEventListener('click', () => {
  const positionSeconds = Number(document.querySelector('#seek-seconds').value);
  if (!Number.isFinite(positionSeconds) || positionSeconds < 0) {
    status.textContent = 'Enter a finite, non-negative position.';
    return;
  }
  runCommand({ name: 'seek', positionSeconds });
});

async function runCommand(command) {
  setControlsDisabled(true);
  status.textContent = 'Sending one command through the Native Host…';
  try {
    const result = await chrome.runtime.sendMessage({ type: 'probeCommand', command });
    status.textContent = result?.accepted === true
      ? 'Accepted once by the active media element.'
      : `Rejected: ${result?.errorCode ?? 'unknown-error'}`;
  } catch {
    status.textContent = 'Rejected: extension-connection-failed';
  } finally {
    setControlsDisabled(false);
  }
}

function setControlsDisabled(disabled) {
  document.querySelectorAll('button, input').forEach((control) => {
    control.disabled = disabled;
  });
}
