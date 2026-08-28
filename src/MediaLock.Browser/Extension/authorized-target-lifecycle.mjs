export function createAuthorizedTargetLifecycle({
  publishTarget,
  publishTargetRemoved,
  clearTab,
}) {
  const targets = new Map();
  const remove = (tabId, reason) => {
    const entry = targets.get(tabId);
    if (!entry) {
      return false;
    }
    targets.delete(tabId);
    clearTab(tabId);
    publishTargetRemoved(entry, reason);
    return true;
  };

  return Object.freeze({
    async replace(entry) {
      const previous = targets.get(entry.target.tabId);
      if (previous) {
        targets.delete(entry.target.tabId);
        publishTargetRemoved(previous, 'target-replaced');
      }
      targets.set(entry.target.tabId, entry);
      await publishTarget(entry);
    },

    remove,

    handleTabUpdated(tabId, changeInfo) {
      return changeInfo?.status === 'loading'
        ? remove(tabId, 'document-replaced')
        : false;
    },

    async observe(target, presentation) {
      const current = targets.get(target?.tabId);
      if (!current || !sameTarget(target, current.target)) {
        return false;
      }
      const updated = Object.freeze({ ...current, presentation });
      targets.set(target.tabId, updated);
      await publishTarget(updated);
      return true;
    },

    get(tabId) {
      return targets.get(tabId);
    },

    values() {
      return targets.values();
    },
  });
}

function sameTarget(candidate, expected) {
  return candidate?.bindingId === expected.bindingId
    && candidate?.endpointId === expected.endpointId
    && candidate?.scope === expected.scope
    && candidate?.tabId === expected.tabId
    && candidate?.frameId === expected.frameId
    && candidate?.documentId === expected.documentId
    && candidate?.pageOrigin === expected.pageOrigin;
}
