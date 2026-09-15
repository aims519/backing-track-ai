# Working with Claude Code on this repo

## Authorship

The ASP.NET Core backend code is mainly written by the repository owner; Claude Code
is used for review, design discussion, and the more mechanical work — unit
tests and refactorings.

The SwiftUI iOS app leans on Claude Code more heavily for code generation, as
the owner is less experienced with the platform; the owner directs and reviews.

`README.md` is author-written.

Visual assets (app icon, launch-screen logo) are produced by a committed script —
`app/Tools/generate_icon.py` — drafted by Claude Code under the owner's art
direction, so the assets stay reproducible and reviewable in a diff. This repo
does not use image-generation-model output for any shipped asset. See
[ADR 0021](docs/adr/0021-code-drawn-visual-assets.md).

The Architecture Decision Records in `docs/adr/` are drafted by Claude Code from
design discussions held in Claude Code sessions, then reviewed by the owner.

## Where the conventions live

- **Architecture decisions and their tradeoffs:** [`docs/adr/`](docs/adr/README.md)
- **Build, test, and deployment:** [`README.md`](README.md), [`api/README.md`](api/README.md)
- **Project scope:** [`docs/backing-track-generator-scope.md`](docs/backing-track-generator-scope.md)

## PR and ADR conventions

Set the **Type:** line to exactly one of the listed PR types.

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

Before opening a PR that touches API code, run `dotnet format api/BackingTrack.sln` and commit
any changes it makes. CI runs `dotnet format whitespace api/BackingTrack.sln --verify-no-changes`
(see `.github/workflows/verify.yml`) and fails the build on drift — catch it locally first rather
than round-tripping through a failed CI run.

## Swift conventions

Assume the reader knows SwiftUI. Comment the *why*, never the *what*:
non-obvious decisions and the alternative rejected, tradeoffs, platform
footguns, server quirks, and links to the relevant ADR. Don't explain
standard language or framework behaviour, restate the line below, or add
ceremony to inits and previews. `MARK:` separators and `///` one-liners
that add signal are welcome.
