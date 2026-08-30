# Working with Claude Code on this repo

<!-- Wordsmith to taste. Kept deliberately short: everything here is public. -->

## Authorship

The ASP.NET Core backend is written mostly by the repository owner; Claude Code
is used for review, design discussion, and the more mechanical work — unit
tests and refactorings.

The SwiftUI iOS app leans on Claude Code more heavily for code generation, as
the owner is less experienced with the platform; the owner directs and reviews.

`README.md` prose is author-written.

The Architecture Decision Records in `docs/adr/` are drafted by Claude Code from
design discussions held in Claude Code sessions, then reviewed by the owner.

## Where the conventions live

- **Architecture decisions and their tradeoffs:** [`docs/adr/`](docs/adr/README.md)
- **Build, test, and deployment:** [`README.md`](README.md), [`api/README.md`](api/README.md)
- **Project scope:** [`docs/backing-track-generator-scope.md`](docs/backing-track-generator-scope.md)

## Notes for Claude

- iOS job states map 1:1 from the backend `JobStates` enum; they are not
  designed independently.
- Prefer deterministic validation of generated music; an LLM critic is a
  later, additive layer, not a first resort.

Local, machine-specific working agreements (not committed) live in
`CLAUDE.local.md`.
