import {
  prepareExactActiveSitePermission,
  requestPreparedSitePermission,
} from './site-permission.mjs';

const status = document.querySelector('#status');
let preparedSitePermission;

prepareExactActiveSitePermission(chrome.tabs).then(
  (result) => {
    preparedSitePermission = result;
  },
  () => {
    preparedSitePermission = { accepted: false, errorCode: 'target-unavailable' };
  },
);

document.querySelector('#authorize-temporary').addEventListener('click', () => {
  authorizeGenericTarget('temporary');
});

document.querySelector('#authorize-site').addEventListener('click', authorizeExactSite);

async function authorizeExactSite() {
  if (!preparedSitePermission) {
    setStatus('Reopen the extension before requesting site access.');
    return;
  }
  setControlsDisabled(true);
  setStatus('Requesting access to this exact site…');
  try {
    const permission = await requestPreparedSitePermission({
      prepared: preparedSitePermission,
      requestPermission: (request) => chrome.permissions.request(request),
    });
    if (permission.accepted !== true) {
      setStatus(`Rejected: ${permission.errorCode ?? 'permission-denied'}`);
      return;
    }
    await authorizeGenericTarget('site');
  } finally {
    setControlsDisabled(false);
  }
}

async function authorizeGenericTarget(scope) {
  setControlsDisabled(true);
  setStatus(scope === 'site'
    ? 'Authorizing one media element for this site…'
    : 'Authorizing one media element on this page…');
  try {
    const result = await chrome.runtime.sendMessage({
      type: 'authorizeGenericTarget',
      scope,
    });
    setStatus(result?.accepted === true
      ? 'Authorized. Select this page in Media Lock to create Session Lock.'
      : `Rejected: ${result?.errorCode ?? 'unknown-error'}`);
  } catch {
    setStatus('Rejected: extension-connection-failed');
  } finally {
    setControlsDisabled(false);
  }
}

function setStatus(text) {
  status.textContent = text;
}

function setControlsDisabled(disabled) {
  document.querySelectorAll('button').forEach((control) => {
    control.disabled = disabled;
  });
}
