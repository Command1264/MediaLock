export async function dispatchBoundCommand({ tabs, documentRegistry, request }) {
  if (!documentRegistry.matches(request.target)) {
    return { accepted: false, errorCode: 'target-unavailable' };
  }

  try {
    return await tabs.sendMessage(
      request.target.tabId,
      request,
      { documentId: request.target.documentId },
    );
  } catch {
    return { accepted: false, errorCode: 'target-unavailable' };
  }
}
