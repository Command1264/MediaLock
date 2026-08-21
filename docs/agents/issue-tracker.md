# Issue tracker: GitHub

Issues and specs for this repository live as GitHub Issues. Use the `gh` CLI after a GitHub remote has been
configured.

Until then, treat the issue tracker as unavailable: plan in approved repository documentation when necessary,
but do not substitute `.scratch/` or another tracker unless the user changes this policy.

## Conventions

- **Create**: `gh issue create --title "..." --body "..."`
- **Read**: `gh issue view <number> --comments`
- **List**: `gh issue list --state open`
- **Comment**: `gh issue comment <number> --body "..."`
- **Label**: `gh issue edit <number> --add-label "..."` or `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`
- Infer the target repository from the configured GitHub remote.

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Publishing and fetching

- “Publish to the issue tracker” means create a GitHub Issue.
- “Fetch the relevant ticket” means read the referenced GitHub Issue and its comments.
- If no GitHub remote exists, report that publishing or fetching is unavailable; do not silently create a
  different tracker.
