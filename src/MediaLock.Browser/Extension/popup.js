import {
  prepareExactActiveSitePermission,
  requestPreparedSitePermission,
} from './site-permission.mjs';

const status = document.querySelector('#status');
let preparedSitePermission;

localizePopup();
initializePopup();

document.querySelector('#authorize-temporary').addEventListener('click', () => {
  authorizeGenericTarget('temporary');
});

document.querySelector('#authorize-site').addEventListener('click', authorizeExactSite);

async function initializePopup() {
  setControlsDisabled(true);
  setStatus(message('statusChecking'));
  try {
    preparedSitePermission = await prepareExactActiveSitePermission(chrome.tabs);
    if (preparedSitePermission.accepted !== true) {
      setStatus(message(
        'statusUnavailable',
        localizedError(preparedSitePermission.errorCode ?? 'page-not-eligible'),
      ));
      return;
    }
    const current = await getCurrentPageBindingStatus(preparedSitePermission.tabId);
    const siteAllowed = current.scope === 'site'
      ? await chrome.permissions.contains(preparedSitePermission.request)
      : undefined;
    if (current.authorized === true && current.scope !== 'site') {
      setStatus(message('statusAuthorized'));
      return;
    }
    if (current.authorized === true && siteAllowed === true) {
      setStatus(message('statusAuthorized'));
      return;
    }
    if (current.authorized === true && current.scope === 'site') {
      setStatus(message('statusNotAuthorized'));
      return;
    }
    const retainedSitePermission = await chrome.permissions.contains(
      preparedSitePermission.request,
    );
    setStatus(message(retainedSitePermission
      ? 'statusSiteAllowedWaiting'
      : 'statusNotAuthorized'));
  } catch {
    preparedSitePermission = { accepted: false, errorCode: 'target-unavailable' };
    setStatus(message('statusUnavailable', localizedError('extension-connection-failed')));
  } finally {
    setControlsDisabled(false);
  }
}

async function getCurrentPageBindingStatus(tabId) {
  try {
    const current = await chrome.tabs.sendMessage(
      tabId,
      { type: 'getGenericEndpointStatus' },
      { frameId: 0 },
    );
    return current?.authorized === true
      ? { authorized: true, scope: current.scope }
      : { authorized: false };
  } catch {
    return { authorized: false };
  }
}

async function authorizeExactSite() {
  if (!preparedSitePermission) {
    setStatus(message('statusReopen'));
    return;
  }
  setControlsDisabled(true);
  setStatus(message('statusRequestingSite'));
  try {
    const permission = await requestPreparedSitePermission({
      prepared: preparedSitePermission,
      requestPermission: (request) => chrome.permissions.request(request),
    });
    if (permission.accepted !== true) {
      setStatus(message(
        'statusRejected',
        localizedError(permission.errorCode ?? 'permission-denied'),
      ));
      return;
    }
    await authorizeGenericTarget('site');
  } finally {
    setControlsDisabled(false);
  }
}

async function authorizeGenericTarget(scope) {
  setControlsDisabled(true);
  setStatus(message(scope === 'site'
    ? 'statusAuthorizingSite'
    : 'statusAuthorizingTemporary'));
  try {
    const result = await chrome.runtime.sendMessage({
      type: 'authorizeGenericTarget',
      scope,
    });
    setStatus(result?.accepted === true
      ? message('statusAuthorized')
      : message('statusRejected', localizedError(result?.errorCode ?? 'unknown-error')));
  } catch {
    setStatus(message('statusRejected', localizedError('extension-connection-failed')));
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

function localizePopup() {
  const uiLanguage = chrome.i18n?.getUILanguage?.();
  if (typeof uiLanguage === 'string' && uiLanguage.length > 0) {
    document.documentElement.lang = uiLanguage.replace('_', '-');
  }
  document.querySelectorAll('[data-i18n]').forEach((element) => {
    const localized = message(element.dataset.i18n);
    if (localized) {
      element.textContent = localized;
    }
  });
}

function message(key, substitution) {
  const localized = chrome.i18n?.getMessage?.(
    key,
    substitution === undefined ? undefined : [substitution],
  );
  if (localized) {
    return localized;
  }
  const fallback = {
    statusChecking: 'Checking current page…',
    statusAuthorized: 'Authorized. Select this page in Media Lock to create Session Lock.',
    statusNotAuthorized: 'Not authorized. Authorize this page to create Session Lock.',
    statusSiteAllowedWaiting: 'This site is allowed. Waiting for one unambiguous media element.',
    statusUnavailable: `Unavailable: ${substitution}`,
    statusReopen: 'Reopen the extension before requesting site access.',
    statusRequestingSite: 'Requesting access to this exact site…',
    statusAuthorizingSite: 'Authorizing one media element for this site…',
    statusAuthorizingTemporary: 'Authorizing one media element on this page…',
    statusRejected: `Rejected: ${substitution}`,
  };
  return fallback[key] ?? '';
}

function localizedError(errorCode) {
  const errors = {
    'page-not-eligible': ['errorPageNotEligible', 'This page cannot be authorized. Open an HTTPS page with one media element. (ML-BR-001)'],
    'extension-connection-failed': ['errorExtensionConnectionFailed', 'The Extension could not check this page. Reload the Extension and page, then try again. (ML-BR-002)'],
    'target-unavailable': ['errorTargetUnavailable', 'The selected media page is no longer available. Reload the page and try again. (ML-BR-003)'],
    'permission-denied': ['errorPermissionDenied', 'Site access was not granted. Review the browser site permission and try again. (ML-BR-004)'],
    'native-host-unavailable': ['errorNativeHostUnavailable', 'Media Lock is not available. Start Media Lock and try again. (ML-BR-005)'],
    'ambiguous-media-elements': ['errorAmbiguousMediaElements', 'More than one media element is available. Leave only one active media element and try again. (ML-BR-006)'],
    'media-element-unavailable': ['errorMediaElementUnavailable', 'No controllable media element is available on this page. (ML-BR-007)'],
    'document-replaced': ['errorDocumentReplaced', 'The page changed during authorization. Wait for it to finish loading and try again. (ML-BR-008)'],
    'unauthorized-command': ['errorUnauthorizedCommand', 'The authorization request is no longer valid. Reopen the Popup and try again. (ML-BR-009)'],
    'play-rejected': ['errorPlayRejected', 'The page rejected playback. Start playback on the page and try again. (ML-BR-010)'],
    'seek-out-of-range': ['errorSeekOutOfRange', 'The requested position is outside the media timeline. (ML-BR-011)'],
    'unknown-error': ['errorUnknown', 'An unexpected Browser Integration error occurred. Try again. (ML-BR-000)'],
  };
  const [key, fallback] = errors[errorCode] ?? errors['unknown-error'];
  return chrome.i18n?.getMessage?.(key) || fallback;
}
