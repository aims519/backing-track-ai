# 0030. Critic judge eval as a separate, code-based project

Status: Accepted

## Context

`BackingTrack.Generation.Eval` (ADR 0027) checks pipeline *outcomes*: does Precise-mode
generation succeed, repair, or fail within N attempts, judged against flat `GoldenSet/*.jsonl`
rows (chords, tempo, key, style) and deterministic, binary checks. ADR 0027's Consequences section
even earmarked a future `CriticTriggering` category for this project once the Theory Critic
landed.

The Critic (`Steps/Critic/`, ADR 0029) is now built but not yet wired into
`GenerationWorkflowBuilder`, so it never runs in the outcome harness's pipeline path. More
importantly, the question worth asking about the Critic isn't an outcome question - it's a judge
question: given a candidate track with a known-correct verdict, does the live LLM critic agree,
and does it agree with itself across repeated calls on the same input? That needs a different
fixture shape entirely: a fully-specified `CandidateTrack` (nested note-event lists) paired with
`MatchedStyle`/`MatchedChord`/`MatchedScale` context and an expected verdict, with one fixture per
`CriticDimension` deliberately isolating a single flaw - not a chord/tempo/style input row that
the pipeline expands into a track.

## Decision

Add `api/eval/BackingTrack.Critic.Eval` as its own console app (also wired into `BackingTrack.sln`)
rather than a category inside `BackingTrack.Generation.Eval`:

- It drives `ICriticStep` directly - no knowledge-base retrieval, generation, validation, repair,
  or persistence wiring, since fixtures supply their own matched style/chords/scales rather than
  going through retrieval.
- Fixtures (`Fixtures/CriticFixtures.cs`) are hand-built C# objects via shared builder helpers
  (`TrackFixtureBuilder`), not JSONL rows - the `CandidateTrack` graph (drum/bass/chord note-event
  lists) doesn't fit a flat golden-set row without becoming a serialized object graph anyway, and
  fixtures here are deliberately engineered (one isolated flaw per `CriticDimension`) rather than
  expected to be hand-edited outside the codebase.
- The runner repeats each fixture N times and reports both correctness (verdict matches the
  fixture's known answer) and consistency (does the verdict/score hold steady across repeats) -
  a second axis ADR 0027's binary outcome checks don't need.

## Alternatives considered

- **Add a `CriticTriggering` `EvalCategory` to `BackingTrack.Generation.Eval`**, as ADR 0027
  anticipated. Rejected: the Critic isn't wired into the workflow, so it can't be exercised this
  way yet; and even once wired, this only tells you whether the *pipeline* reacted correctly to a
  critic verdict, not whether the verdict itself was right or stable - the golden-set row also
  can't represent a fully-specified `CandidateTrack` with an isolated flaw without effectively
  becoming this project's fixture shape anyway.
- **Keep fixtures as JSONL**, deserialized into `CandidateTrack`/`MatchedChord`/`MatchedScale`.
  Rejected for now: `CandidateTrack` is a nested note-event graph, not a flat row: hand-typing it
  as JSON (e.g. eighth-note hihat timestamps by hand) is more error-prone than code and loses
  compile-time checking, and shared C# builder helpers keep fixtures internally consistent - which
  caught a real bug on first use (a drum pattern that contradicted its own style description).

## Consequences

- Two live-only eval projects now exist, each un-wired to CI (both cost LLM tokens per run;
  record/replay cassettes, per ADR 0027, are a prerequisite for either going into CI). Separate
  projects mean separate future CI trigger scoping (e.g. path-filtered so a critic-prompt-only
  change doesn't wait on the full generation suite) and separate pass/fail gates, at the cost of
  two workflow/secrets setups instead of one.
- Fixtures aren't hand-editable without a rebuild, and a shared builder change (like the drum
  pattern fix above) can silently affect every fixture that uses it - a JSONL row's diff would be
  more isolated. Acceptable while fixtures are small and hand-engineered.
- If most future fixtures end up being captured real production `CandidateTrack`s that triggered a
  critic disagreement (rather than hand-engineered synthetic ones), JSONL - or loading captured
  `CandidateTrack` JSON blobs with metadata still in C# - may become the better fit, and this
  decision should be revisited then.
- The first run already validated the approach: it surfaced a real fixture bug (a drum pattern
  contradicting its own style description) and, once fixed, two genuine Critic-quality findings -
  non-deterministic pass/fail on a borderline candidate across repeats, and over-strict reasoning
  conflating "root not in the bass" with "root omitted" on inverted voicings.
