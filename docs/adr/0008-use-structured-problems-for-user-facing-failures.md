# Use structured problems for user-facing failures

## Status

Accepted for Phase 17 implementation on 2026-08-28.

## Context

Media Lock previously passed English warning and exception strings through Application state and Route Decisions.
WPF surfaces compared those strings to preserve dismissal state, Settings exposed raw adapter exceptions, and logs had
no stable identifier that a user could quote. Changing language could not reliably re-render an already visible
failure. Exception messages could also contain private paths or other context unsuitable for primary UI copy.

Core must remain independent of WPF and localization, while the main window, Settings, tray, startup dialogs,
structured logs and support diagnostics must identify the same semantic failure.

## Decision

Application owns an immutable `MediaLockProblem` contract. Its semantic `MediaLockProblemId` resolves to one public,
compatibility-stable code and default severity through `MediaLockProblemCatalog`. Each occurrence receives a separate
process-local occurrence ID, so dismissing one occurrence does not suppress a later recurrence of the same public
code. Optional technical context is limited to the exception type; exception messages are not user copy and are not
part of the standard problem diagnostic.

The App presentation layer maps the semantic identifier to exact English and Traditional Chinese resources at read
time, then appends the public code. Missing locale resources fall back to English without changing the code; an
unknown identifier uses the identifiable `ML-APP-000` fallback. Language changes therefore re-render the same
problem object instead of rewriting state.

Core Route Decisions retain their existing semantic reason and may expose only an exception type for a failed
provider call. They do not carry localized text or raw exception messages. Application state carries a structured
problem rather than `ErrorMessage` or catalog prose. A diagnostic event may carry `ProblemCode`; the application
remembers the latest reported code separately from active UI state so the privacy-safe diagnostic summary can include
the same code without promoting a Settings or tray failure into the main-window error card.

Existing Browser Integration `ML-BR-000` through `ML-BR-011` codes remain stable. Desktop authorization failures
continue that namespace at `ML-BR-012` and later rather than renumbering the Extension codes.

## Consequences

Adding a user-facing warning or error requires one catalog entry, English and Traditional Chinese resources, tests
and public catalog documentation. Published codes are never reused for another meaning. Message wording and recovery
guidance may improve without changing the code, while a semantic split receives new codes.

The contract adds deliberate mapping at presentation boundaries, but removes string parsing, makes immediate language
changes deterministic and prevents raw exception details from becoming the primary message. It does not change Router
decisions, Recovery, persistence schemas, Media Target identity, telemetry or remote reporting.
