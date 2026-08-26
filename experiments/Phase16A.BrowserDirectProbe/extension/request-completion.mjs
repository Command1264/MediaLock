export function completePendingRequest({
  pendingRequests,
  requestId,
  outcome,
  sendResult,
}) {
  if (!pendingRequests.has(requestId)) {
    return false;
  }

  sendResult();
  return pendingRequests.complete(requestId, outcome);
}
