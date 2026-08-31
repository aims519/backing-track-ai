# Working with Claude Code on this repo

## Authorship

The ASP.NET Core backend code is mainly written by the repository owner; Claude Code
is used for review, design discussion, and the more mechanical work — unit
tests and refactorings.

The SwiftUI iOS app leans on Claude Code more heavily for code generation, as
the owner is less experienced with the platform; the owner directs and reviews.

`README.md` is author-written.

The Architecture Decision Records in `docs/adr/` are drafted by Claude Code from
design discussions held in Claude Code sessions, then reviewed by the owner.

## Where the conventions live

- **Architecture decisions and their tradeoffs:** [`docs/adr/`](docs/adr/README.md)
- **Build, test, and deployment:** [`README.md`](README.md), [`api/README.md`](api/README.md)
- **Project scope:** [`docs/backing-track-generator-scope.md`](docs/backing-track-generator-scope.md)

## PR and ADR conventions

Tick exactly one PR type checkbox.

For the Architectural Decision section, evaluate honestly: did this
involve a genuine alternative, a costly-to-reverse choice, or something
a new engineer would ask "why this way" about? If so, set it to the
ADR reference and draft an ADR stub before finalizing the PR — don't
default to "N/A" or "No" for convenience. Use "No" when a decision was
made but needs no ADR, "N/A" when nothing architectural was involved.

Set the Testing section to "N/A" only for docs/chore-only changes with
nothing to verify.

Do not add an authorship/attribution footer (e.g. "Generated with
Claude Code") to PR bodies or commit messages. Authorship is
documented once, deliberately, in this file.

If applicable, link the PR to its Notion task. Match based on task name as order numbers can shift.

## Swift conventions

Explain non-trivial Swift idioms with brief inline comments — the owner
directs and reviews this code but is less experienced with the platform,
and needs to be able to read and understand it without external lookup.
