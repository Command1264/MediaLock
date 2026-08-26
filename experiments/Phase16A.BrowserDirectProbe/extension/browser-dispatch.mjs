export async function dispatchBoundCommand({
  tabs,
  documentRegistry,
  genericTargetRegistry,
  request,
}) {
  const isGenericTarget = typeof request.target?.bindingId === 'string'
    && typeof request.target?.endpointId === 'string';
  const targetMatches = isGenericTarget
    ? genericTargetRegistry?.matches(request.target) === true
    : documentRegistry.matches(request.target);
  if (!targetMatches) {
    return { accepted: false, errorCode: 'target-unavailable' };
  }

  try {
    return await tabs.sendMessage(
      request.target.tabId,
      isGenericTarget ? { ...request, type: 'genericCommand' } : request,
      { documentId: request.target.documentId },
    );
  } catch {
    return { accepted: false, errorCode: 'target-unavailable' };
  }
}
