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
- Fingerprint ranking requires the stable source descriptor, treats an instance hint as the strongest match, and
  then uses matching title／artist, playback status and observation-time proximity only as confidence signals.
  Track metadata can rank candidates but never qualifies a different source or mismatched stable instance hint.
- App Lock selects deterministically: Playing first, then newest observation, then ordinal Session key.
- A missing Locked Target enters Recovering. A unique Fingerprint successor restores Session Lock.
- A Recovery epoch stays stable across unrelated catalog refreshes. Its timeout remains effective until recovery or
  policy resolution clears the epoch; later stale timeout intents are ignored.
- When a result first enters Recovering, the caller schedules exactly one `RecoveryTimedOut(epoch)` intent after the
  configured timeout. Catalog refreshes do not restart that timer; leaving Recovering cancels the caller's pending
  timer, while a stale timeout is safe because Core ignores epochs that are no longer active.
- Fallback Policy values are Wait, Same Application, Windows Current Session, Same Application then Windows Current,
  and Disable Routing. The product default waits 15 seconds, tries Same Application, then uses Windows Current;
  every applied outcome has a distinct observable status or route reason.

## Ordering and cancellation

`MediaRouter` uses one single-reader queue. Concurrent callers are processed in submission order with at most one
media control in flight. Canceling a queued intent completes that caller promptly, skips the canceled work when it
reaches the reader and leaves later intents operational. Disposing the router closes the writer and cancels work
that is still in flight.

## Persistence schemas

`MediaLockSettings` and `RuntimeStateDocument` begin at schema version 1. Both documents return path-specific,
actionable validation issues for unsupported versions, invalid enum values, missing sections and inconsistent
Locked Target state. Runtime state stores the complete persisted Fingerprint inputs used for recovery confidence:
source application ID, optional stable instance hint, playback status, observation timestamp, title and artist. It
never stores a live Session key or GSMTC object.

JSON serialization and atomic file replacement belong to the Windows persistence adapter and are scheduled for a
later phase; Phase 1 defines the platform-independent documents and validation rules only.
