# 0021. Code-drawn visual assets, no image-model output

Status: Accepted

## Context

The iOS app needs an app icon and a launch screen (see
[`docs/backing-track-generator-scope.md`](../backing-track-generator-scope.md); no App Store
distribution, so this is for on-device builds, not store review). There is no designer on the
project and no brand identity to apply — the mark just needs to exist, be recognisable, and be
good enough to iterate on later.

This is a portfolio project, and authorship is a stated concern: `CLAUDE.md` already documents
who writes what (owner writes most of the API, Claude generates more of the iOS code under
review, ADRs are Claude-drafted). Visual assets raise the same question in a sharper form,
because the obvious quick path — a diffusion image model — produces an artefact whose provenance
can't be shown in a diff.

## Decision

Produce the icon and launch-screen logo from a committed script,
[`app/Tools/generate_icon.py`](../../app/Tools/generate_icon.py) (Pillow), that draws the mark
with geometric primitives. The generated PNGs are committed next to the script. No
image-generation-model output is used for any shipped asset, here or elsewhere in the repo.

Documented in `CLAUDE.md` (Authorship) and
[`app/BackingTrack/Resources/README.md`](../../app/BackingTrack/Resources/README.md).

## Alternatives considered

- **Diffusion image model (DALL·E / Midjourney / a Hugging Face Space).** Fastest route to a
  polished-looking icon. Rejected: the output isn't reproducible (no seed-stable pipeline kept in
  the repo), can't be meaningfully code-reviewed, and carries unresolved training-data/copyright
  questions. For a portfolio repo that makes a point of documenting authorship, an asset whose
  origin is "a model produced this, prompt not kept" is the wrong signal — and iterating on it
  means re-rolling, not editing.
- **A hand-drawn asset (Figma / Sketch / an SVG editor), exported to PNG and committed.** The
  normal way to do this. Rejected for practical reasons: no design tool or SVG toolchain in the
  environment, and the source file (`.fig`, or an SVG hand-edited without a canvas) would be about
  as reviewable as the PNG anyway. A Python script that draws the shape is more legible as a diff
  than SVG path data, and regenerates all sizes in one run.
- **Ship without a custom icon** (Xcode's blank placeholder, blank launch screen). Rejected:
  cheap to fix, and the blank icon looks broken on-device.

## Consequences

- **The mark is constrained to what's cheap to express as primitives** — polygons, ellipses,
  gradients. The current icon (a cog with a beamed pair of eighth notes) took several iterations
  to stop looking like stuck-together shapes, and a genuinely illustrative icon would be painful
  this way. It's explicitly a v1 placeholder.
- **Pillow is now a dev dependency** for regenerating assets. It's not in any build or CI path —
  the committed PNGs are what the app uses — but a contributor changing the mark needs
  `pip install pillow`.
- **Two sources of the accent colour to keep in step.** The icon gradient comes from `ACCENTS` in
  the script; `AccentColor` and `LaunchBackground` in the asset catalog are hand-set. Changing the
  brand colour means editing both. Noted in the Resources README.
- **In exchange:** every asset change is a readable code diff, all sizes regenerate deterministically
  from one command, and the provenance question has a clear answer.
