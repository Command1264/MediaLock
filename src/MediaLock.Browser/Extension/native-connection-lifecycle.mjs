export function handleNativePortDisconnect(runtime, resetConnection) {
  const errorMessage = runtime.lastError?.message ?? null;
  resetConnection({ disconnectPort: false });
  return errorMessage;
}
