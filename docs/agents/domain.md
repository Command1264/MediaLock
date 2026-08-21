# Domain Docs

Media Lock uses a single domain context.

## Before exploring

- Read root `CONTEXT.md` for canonical domain vocabulary.
- Read ADRs under `docs/adr/` that affect the area being changed.
- If either location is absent, proceed without treating that absence as an error.

## Consumer rules

- Use the canonical terms defined in `CONTEXT.md`; avoid synonyms explicitly listed there.
- Keep implementation details out of `CONTEXT.md`.
- Record only durable, costly, non-obvious architectural decisions as ADRs.
- Surface conflicts with an existing ADR instead of silently overriding it.
