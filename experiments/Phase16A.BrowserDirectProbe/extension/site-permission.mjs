export async function prepareExactActiveSitePermission(tabs) {
  const activeTabs = await tabs.query({ active: true, currentWindow: true });
  if (activeTabs.length !== 1
      || !Number.isSafeInteger(activeTabs[0].id)
      || typeof activeTabs[0].url !== 'string') {
    return { accepted: false, errorCode: 'target-unavailable' };
  }

  let pageUrl;
  try {
    pageUrl = new URL(activeTabs[0].url);
  } catch {
    return { accepted: false, errorCode: 'page-not-eligible' };
  }
  if (pageUrl.protocol !== 'https:') {
    return { accepted: false, errorCode: 'page-not-eligible' };
  }

  return {
    accepted: true,
    request: { origins: [`${pageUrl.origin}/*`] },
  };
}

export async function requestPreparedSitePermission({ prepared, requestPermission }) {
  if (prepared?.accepted !== true) {
    return prepared ?? { accepted: false, errorCode: 'page-not-eligible' };
  }
  return await requestPermission(prepared.request) === true
    ? { accepted: true }
    : { accepted: false, errorCode: 'permission-denied' };
}
