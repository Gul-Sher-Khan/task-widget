# AGENTS.md

Shared instructions for every coding agent working in this repo. This file is the source of truth; tool-specific files (e.g. `CLAUDE.md`) point here instead of duplicating it.

## Prototype fidelity

A prototype the user has marked finalized is binding. When implementing a spec or ticket that links one, reproduce it exactly: layout, spacing, colours, typography, copy, motion timings and easing, states. Copy its code where the stack allows; don't reinterpret, simplify, or "clean up" the design. If something in it can't be built as-is, stop and ask the user. Don't substitute your own version.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Uses the five default triage labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
