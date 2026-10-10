# Working with Claude Code on this repo

## Authorship

The ASP.NET Core backend code is mainly written by the repository owner; Claude Code
is used for review, design discussion, and the more mechanical work — unit
tests and refactorings.

The SwiftUI iOS app leans on Claude Code more heavily for code generation, as
the owner is less experienced with the platform; the owner directs and reviews.

`README.md` prose is mainly author-written; some sections were drafted by Claude Code
and then edited by the owner. Claude Code also drafts commit messages and PR
descriptions, and built the architecture diagrams in `docs/images/` through the
Lucid connector (exported by the owner).

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

Name branches with a type prefix — `feature/`, `fix/` or `chore/` — followed by a short
kebab-case description, e.g. `feature/critic-call-metrics`.

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

Before opening a PR that touches API code, run `dotnet format whitespace api/BackingTrack.sln`
and commit any changes it makes — that's exactly what CI verifies (see
`.github/workflows/verify.yml`, `dotnet format whitespace api/BackingTrack.sln --verify-no-changes`),
so running it locally catches drift before a failed CI round-trip. Run the plain `dotnet format`
(no `whitespace` subcommand) only when asked to fix analyzer/style issues as its own task — it
also applies analyzer-driven code fixes CI doesn't check for, which pulls unrelated files into an
otherwise-unrelated diff.

Before opening a PR that touches the iOS app (`app/`), ask the owner whether `MARKETING_VERSION`
in `app/project.yml` should be bumped. The merge to `main` ships it to TestFlight (ADR 0040), so
this is the last easy moment to decide. Don't bump it without asking. Leave the build number
alone: CI sets it.

The API's version is not bumped in code. MinVer derives it from `api-v*` git tags plus the commit
(ADR 0046), so don't add or edit a `<Version>` in the API projects. Don't create or push tags
either: cutting a release (`git tag api-vX.Y.Z`) is the owner's call, made after merge.

The iOS app's version stays in `MARKETING_VERSION` (`app/project.yml`); `ios-v*` tags are labels on
the merge commit that bumped it, named exactly `ios-v` plus that value (`ios-v0.7.3`). Same rule:
don't create or push them. `ios-tag-check.yml` fails a tag that doesn't match `project.yml`.

## Swift conventions

Assume the reader knows SwiftUI. Comment the *why*, never the *what*:
non-obvious decisions and the alternative rejected, tradeoffs, platform
footguns, server quirks, and links to the relevant ADR. Don't explain
standard language or framework behaviour, restate the line below, or add
ceremony to inits and previews. `MARK:` separators and `///` one-liners
that add signal are welcome.
