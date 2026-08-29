export function createGrantedSiteBindingHandler({ tabs, bindCompletedTab }) {
  if (typeof tabs?.query !== 'function' || typeof bindCompletedTab !== 'function') {
    throw new TypeError('Granted-site binding dependencies are required.');
  }

  return async (added) => {
    const origins = new Set(
      (Array.isArray(added?.origins) ? added.origins : [])
        .map(exactHttpsOrigin)
        .filter((origin) => origin !== null),
    );
    if (origins.size === 0) {
      return [];
    }

    const openTabs = await tabs.query({});
    const matchingTabs = openTabs.filter((tab) => {
      if (!Number.isSafeInteger(tab?.id)
          || tab?.status !== 'complete'
          || typeof tab?.url !== 'string') {
        return false;
      }
      try {
        return origins.has(new URL(tab.url).origin);
      } catch {
        return false;
      }
    });
    return Promise.all(matchingTabs.map((tab) => bindCompletedTab(tab)));
  };
}

export function createTrustedSiteReconciler({
  tabs,
  hasSitePermission,
  bindCompletedTab,
}) {
  if (typeof tabs?.query !== 'function'
      || typeof hasSitePermission !== 'function'
      || typeof bindCompletedTab !== 'function') {
    throw new TypeError('Trusted-site reconciliation dependencies are required.');
  }

  return async () => {
    const openTabs = await tabs.query({});
    const candidates = openTabs.filter((tab) => Number.isSafeInteger(tab?.id)
      && tab?.status === 'complete'
      && exactHttpsOriginFromUrl(tab?.url) !== null);
    const authorizedTabs = (await Promise.all(candidates.map(async (tab) => {
      const origin = exactHttpsOriginFromUrl(tab.url);
      return await hasSitePermission(origin) === true ? tab : null;
    }))).filter((tab) => tab !== null);
    const results = await Promise.all(authorizedTabs.map((tab) => bindCompletedTab(tab)));
    return Object.freeze({ eligibleCount: authorizedTabs.length, results });
  };
}

function exactHttpsOrigin(pattern) {
  if (typeof pattern !== 'string' || !pattern.endsWith('/*')) {
    return null;
  }
  const candidate = pattern.slice(0, -2);
  try {
    const url = new URL(candidate);
    return url.protocol === 'https:'
      && !url.hostname.includes('*')
      && url.origin === candidate
      ? url.origin
      : null;
  } catch {
    return null;
  }
}

function exactHttpsOriginFromUrl(value) {
  if (typeof value !== 'string') {
    return null;
  }
  try {
    const url = new URL(value);
    return url.protocol === 'https:' ? url.origin : null;
  } catch {
    return null;
  }
}
