# 0020. Structured chord picker over free-text entry on the generation input screen

Status: Accepted

## Context

The main input screen (`GenerateTrackView`) collects the parameters for a Precise-mode backing
track, including the chord progression, and POSTs them to `/tracks/generate`.

The API validates each chord symbol server-side in `ChordSymbolParser`: a root note (`A`–`G`,
optionally `#`/`b`) followed by a quality that must be one of a fixed set of ids from
`chords.jsonl` (`min`, `7`, `maj7`, `min7`, `sus2`, `sus4`, `dim`, `6`, `9`, or empty for major).
Anything outside that grammar comes back as a `400` with per-chord error messages.

So the screen needs a chord-entry control, and the question was how much structure to impose on
it. The scope doc lists a "chord/fretboard visualization component" only as a deferred stretch
goal, so a full visual fretboard builder was out of scope for this screen regardless.

## Decision

Chords are entered through a constrained picker, never as free text. An "Add chord" sheet has two
wheel pickers — root note (12 chromatic values, sharp spelling) and quality (the exact set the
API accepts, as the `ChordQuality` enum) — and appends to the progression. The progression is
shown as a flow of draggable chips (not a `List` — see Consequences); reordering is drag-and-drop
and each chip carries its own delete control.

Because every chord is assembled from a valid root and a valid quality, the app never constructs
an invalid symbol, so there is **no client-side port of `ChordSymbolParser`** — the picker's type
system is the validation.

The `Chord` model carries two projections:

- `displaySymbol` — musician-facing notation (`Am7`, `F♯`), used in the list.
- `apiSymbol` — what the wire format needs (`Amin7`, `F#7`), used only when building the request.

These differ for minor chords (`m`/`m7` vs. the API's `min`/`min7`) and for the sharp glyph
(`♯` vs. ASCII `#`).

## Alternatives considered

**Free-text field** (`"C Am F G7"`, split on whitespace) — rejected. The owner ruled it out
directly. It needs the app to reimplement and stay in sync with `ChordSymbolParser`'s grammar;
mobile keyboards make chord symbols ("Cmaj7", "F#m7b5") slow and error-prone to type; and it
offers no discoverability of which qualities are actually supported. The failure mode is a
round-trip to the server to be told a symbol was wrong.

**Free-text with a live parsed-chip preview** — rejected for the same grammar-duplication cost,
for marginal benefit over the plain field once a structured picker was on the table.

**Fretboard / visual chord builder** — rejected for now. It's a deferred stretch goal in the
scope doc, and a much larger UI investment than this screen warrants while the generation
pipeline itself is still being built.

**Key-aware chord suggestions** (offer only the diatonic chords for the chosen key) — rejected
for this pass. The API places no such restriction, borrowed and modal chords are common in real
progressions, and the picker is already short. Could be added later as a shortcut layer on top of
the full picker without changing the model.

## Consequences

- No client-side chord parser to maintain. The picker can only produce symbols the API accepts,
  so the `Chords` field of a submitted request never fails validation on format. (Other fields
  can still fail — e.g. tempo out of range — so request-level error handling is still needed when
  networking is wired up.)
- The `displaySymbol` / `apiSymbol` split is a small ongoing tax: any new `ChordQuality` case
  must define both, and the divergence (`m` vs `min`) is a thing a reader has to notice. A test
  (`ChordTests.everyQualityMapsToASuffixTheApiAccepts`) pins the API side to the parser's
  accepted set so a bad mapping fails in CI rather than at runtime.
- Uncommon-but-valid chords are only reachable if their quality is in the `ChordQuality` enum.
  Extended/altered chords the API doesn't model yet (9ths beyond dominant, `m7b5`, `add9`, slash
  chords) can't be entered — which is correct today, since the API would reject them anyway, but
  means the enum and `chords.jsonl` have to grow together.
- If a fretboard builder is added later (the stretch goal), it becomes an alternative *input
  method* feeding the same `[Chord]` model — this decision doesn't block it.
- The progression is rendered as a custom chip flow rather than a `List`. `List`'s reordering
  (`.onMove`) needs an explicit edit mode on iOS, and its drag-and-drop modifiers don't work for
  a subview of a `List`/`Form` — so the whole input screen is a `ScrollView` of hand-built
  section cards, and the chips use `.draggable` plus a single container `.dropDestination` that
  turns the drop point into an insertion index. More UI code than a `List` row would have been,
  in exchange for the interaction the screen wanted.
