export function createTrustedSiteAutoBinding({
  hasTarget,
  hasSitePermission,
  bindTab,
  commitBinding,
  discardBinding,
}) {
  const pendingTabs = new Map();
  const generations = new Map();
  const invalidate = (tabId) => {
    if (!Number.isSafeInteger(tabId)) {
      return;
    }
    generations.set(tabId, (generations.get(tabId) ?? 0) + 1);
    pendingTabs.delete(tabId);
  };

  return Object.freeze({
    handleTabUpdated(tabId, changeInfo, tab) {
      if (changeInfo?.status === 'loading') {
        invalidate(tabId);
        return Promise.resolve({ accepted: false, errorCode: 'document-replaced' });
      }
      if (changeInfo?.status !== 'complete'
          || !Number.isSafeInteger(tabId)
          || tab?.id !== tabId
          || hasTarget(tabId)) {
        return Promise.resolve({ accepted: false, errorCode: 'target-unavailable' });
      }

      let pageUrl;
      try {
        pageUrl = new URL(tab.url);
      } catch {
        return Promise.resolve({ accepted: false, errorCode: 'page-not-eligible' });
      }
      if (pageUrl.protocol !== 'https:') {
        return Promise.resolve({ accepted: false, errorCode: 'page-not-eligible' });
      }

      const generation = generations.get(tabId) ?? 0;
      const pending = pendingTabs.get(tabId);
      if (pending?.generation === generation) {
        return pending.promise;
      }
      const operation = Promise.resolve()
        .then(async () => {
          if (await hasSitePermission(pageUrl.origin) !== true
              || (generations.get(tabId) ?? 0) !== generation
              || hasTarget(tabId)) {
            return { accepted: false, errorCode: 'permission-denied' };
          }
          const result = await bindTab(tab);
          if (result?.accepted !== true) {
            return result;
          }
          if ((generations.get(tabId) ?? 0) !== generation || hasTarget(tabId)) {
            discardBinding(result.target);
            return { accepted: false, errorCode: 'document-replaced' };
          }
          await commitBinding(result);
          return { accepted: true };
        })
        .finally(() => {
          if (pendingTabs.get(tabId)?.promise === operation) {
            pendingTabs.delete(tabId);
          }
        });
      pendingTabs.set(tabId, { generation, promise: operation });
      return operation;
    },

    invalidate,
  });
}
