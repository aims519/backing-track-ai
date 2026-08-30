# 0019. XcodeGen-generated project over a committed .xcodeproj

Status: Accepted

## Context

Scaffolding `app/`, the SwiftUI iOS app (see
[`docs/backing-track-generator-scope.md`](../backing-track-generator-scope.md)). Xcode's normal
workflow is to create the project through its GUI wizard and commit the resulting `.xcodeproj`
(really a bundle wrapping a `project.pbxproj` file — an old-style plist). That file is being
authored here without access to the Xcode GUI, and for this phase Claude is writing most of this
app's code rather than the user, who is newer to SwiftUI/Xcode tooling.

## Decision

Define the project in a checked-in `app/project.yml` and generate the `.xcodeproj` from it with
[XcodeGen](https://github.com/yonaskolb/XcodeGen) (`xcodegen generate`). The generated
`.xcodeproj` is gitignored, not committed — regenerated locally after cloning or after
`project.yml` changes.

## Alternatives considered

- **Commit a hand-authored `.xcodeproj` directly.** `project.pbxproj` is a verbose, plist-like
  format (file references, build phases, and group hierarchy all cross-referenced by opaque hex
  IDs) never designed for hand-editing or review. Diffs are noisy relative to the actual change,
  merge conflicts on the same file are common on real teams, and a slipped edit can corrupt the
  project without an obvious error. That risk is worse here: Claude is generating most of the
  project structure, and the user has less SwiftUI/Xcode background to catch a bad hand-edit.
- **Tuist**, a similar project-generation tool. Comparable capability to XcodeGen; XcodeGen was
  chosen for the simpler YAML spec (Tuist's is Swift-code-based, more powerful but more moving
  parts) for a single-target MVP app.

## Consequences

- **New machine-setup step.** Cloning the repo isn't enough to open the project — `brew install
  xcodegen` and `xcodegen generate` are required first (documented in
  [`app/README.md`](../../app/README.md)).
- **Regeneration discipline.** Adding, removing, or moving a source file, or changing any project
  setting, means editing `project.yml` and re-running `xcodegen generate` — Xcode's own
  "just drag a file in" won't update `project.yml`, so the two can drift if a file is added purely
  through Xcode and never reflected back.
- **Clean diffs.** In exchange, every project-structure change is a readable YAML diff instead of
  a pbxproj diff, and there's nothing to merge-conflict on the generated file.
