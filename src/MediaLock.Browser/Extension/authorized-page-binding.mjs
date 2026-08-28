export function createAuthorizedPageBindingCoordinator({
  hasTarget,
  hasSitePermission,
  bindTab,
  commitBinding,
  discardBinding,
}) {
  const pendingTabs = new Map();
  const generations = new Map();
  const operationTails = new Map();
  const invalidate = (tabId) => {
    if (!Number.isSafeInteger(tabId)) {
      return;
    }
    generations.set(tabId, (generations.get(tabId) ?? 0) + 1);
    pendingTabs.delete(tabId);
  };

  const enqueue = (tabId, action) => {
    const previous = operationTails.get(tabId) ?? Promise.resolve();
    const operation = previous
      .catch(() => undefined)
      .then(action);
    const tail = operation
      .catch(() => undefined)
      .finally(() => {
        if (operationTails.get(tabId) === tail) {
          operationTails.delete(tabId);
        }
      });
    operationTails.set(tabId, tail);
    return operation;
  };

  const bindCurrent = async ({
    tab,
    scope,
    generation,
    permissionChecked = false,
    rejectExistingTarget = false,
  }) => {
    let pageUrl;
    try {
      pageUrl = new URL(tab?.url);
    } catch {
      return { accepted: false, errorCode: 'page-not-eligible' };
    }
    if (pageUrl.protocol !== 'https:') {
      return { accepted: false, errorCode: 'page-not-eligible' };
    }
    if (scope === 'site'
        && permissionChecked !== true
        && await hasSitePermission(pageUrl.origin) !== true) {
      return { accepted: false, errorCode: 'permission-denied' };
    }

    const result = await bindTab(tab, scope);
    if (result?.accepted !== true) {
      return result;
    }
    if (scope === 'site' && await hasSitePermission(pageUrl.origin) !== true) {
      discardBinding(result.target);
      return { accepted: false, errorCode: 'permission-denied' };
    }
    if ((generations.get(tab.id) ?? 0) !== generation
        || (rejectExistingTarget && hasTarget(tab.id))) {
      discardBinding(result.target);
      return { accepted: false, errorCode: 'document-replaced' };
    }
    await commitBinding(result);
    return { accepted: true };
  };

  return Object.freeze({
    authorizeTab({ scope, tab }) {
      if ((scope !== 'temporary' && scope !== 'site')
          || !Number.isSafeInteger(tab?.id)) {
        return Promise.resolve({ accepted: false, errorCode: 'target-unavailable' });
      }
      invalidate(tab.id);
      const generation = generations.get(tab.id);
      return enqueue(tab.id, () => bindCurrent({ tab, scope, generation }));
    },

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
      const operation = enqueue(tabId, async () => {
          if (await hasSitePermission(pageUrl.origin) !== true
              || (generations.get(tabId) ?? 0) !== generation
              || hasTarget(tabId)) {
            return { accepted: false, errorCode: 'permission-denied' };
          }
          return bindCurrent({
            tab,
            scope: 'site',
            generation,
            permissionChecked: true,
            rejectExistingTarget: true,
          });
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
