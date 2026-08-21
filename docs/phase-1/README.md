# Phase 1 Core

Phase 1 implements the platform-independent routing model in `MediaLock.Core`. The module targets plain `net10.0`
and has no WPF, WinRT or Win32 dependency.

## Public seam

Callers submit every catalog change, lock action, recovery deadline and Media Command through:

```csharp
ValueTask<RouterResult> DispatchAsync(
    RouterIntent intent,
    CancellationToken cancellationToken);
```

The returned `RouterResult` contains the immutable state after that intent and one explicit `RouteDecision`.
Callers do not retain live GSMTC objects, rank candidates, apply fallback policy, or coordinate concurrent work.
The Windows layer implements `IMediaController`, which receives a resolved ephemeral `SessionKey` only after Core
has checked routing state and command capability.

## State and identity

- Windows Auto resolves Windows Current Session at command time.
- Session Lock preserves a `SessionFingerprint`; an ephemeral `SessionKey` is never persisted as identity.
- App Lock selects deterministically: Playing first, then newest observation, then ordinal Session key.
- A missing Locked Target enters Recovering. A unique Fingerprint successor restores Session Lock.
- A recovery timeout includes the scheduling state revision, so stale timeout intents are ignored.
- Fallback Policy values are Wait, Same Application, Windows Current Session and Disable Routing. Every outcome has
  a distinct observable status or route reason.

## Ordering and cancellation

`MediaRouter` uses one single-reader queue. Concurrent callers are processed in submission order with at most one
media control in flight. Canceling a queued intent completes that caller promptly, skips the canceled work when it
reaches the reader and leaves later intents operational. Disposing the router closes the writer and cancels work
that is still in flight.

## Persistence schemas

`MediaLockSettings` and `RuntimeStateDocument` begin at schema version 1. Settings validation returns path-specific,
actionable issues. Runtime state stores only a persisted Locked Target descriptor (`SourceAppUserModelId` plus an
optional stable instance hint), never a live Session key or GSMTC object.

JSON serialization and atomic file replacement belong to the Windows persistence adapter and are scheduled for a
later phase; Phase 1 defines the platform-independent documents and validation rules only.
