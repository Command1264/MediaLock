# Phase 11B documented mirror probe

This disposable WinForms probe uses only documented desktop SMTC interop for the mirror. It runs the real
`MediaLockApplication` and `MediaRouter`, excludes its own executable Session from the GSMTC catalog, and forwards each
Windows surface button or seek request once with the capture-time routed target.

The published title is prefixed with `[Media Lock Mirror]` so the Windows surface can be distinguished visually from
the source application's native Session.

The mirror starts disabled. Select an external Session, lock it, then enable the mirror. `Inspect Windows surface`
records the public Current Session and enumeration order separately. This experiment is not part of `MediaLock.sln`
and does not promise that Windows will select or rank the mirror first.
