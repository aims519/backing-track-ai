# BackingTrack.Critic.Eval

Console harness for the Theory Critic (`BackingTrack.Generation/Steps/Critic`) specifically —
separate from `BackingTrack.Generation.Eval`, which checks pipeline *outcomes* end-to-end. This
project checks the LLM judge itself: given a hand-authored candidate track with a known-correct
verdict, does the critic agree, and does it keep agreeing with itself across repeated calls?

## Fixtures

`Fixtures/CriticFixtures.cs` holds a small set of hand-built `CandidateTrack`s, each paired with
the verdict a competent human reviewer would give it:

- Two known-good tracks (correct chord tones, sensible voice leading, a real groove) that should
  pass every dimension.
- One deliberately-flawed track per `CriticDimension`, each flaw isolated so a wrong verdict
  points at a specific gap rather than "something's off":
  - `VoiceLeading` — the same three-note cluster held across all four bars regardless of the
    chord progression underneath it (the bug that originally motivated the critic step).
  - `ChordToneAdherence` — voicings sit a semitone off every chord tone of the underlying chord.
  - `RhythmAndFeel` — correct, well-voice-led chord tones, but bass/drums collapse to one flat,
    unaccented hit per bar.

Add more fixtures as real generations surface new failure modes worth locking in.

## Running

Costs LLM tokens on every run - not part of CI, run it manually or as a nightly job alongside
`BackingTrack.Generation.Eval`.

```
dotnet user-secrets set OpenAi:ApiKey "sk-..." --project api/eval/BackingTrack.Critic.Eval
dotnet run --project api/eval/BackingTrack.Critic.Eval -- --repeat 5
```

`--id <id>` restricts the run to one fixture (repeatable); `--repeat <n>` controls how many times
each fixture is reviewed (default 5) to measure consistency, not just correctness; `-v`/`--verbose`
also surfaces `BackingTrack.Generation` internals (e.g. the LLM request/response detail behind
each `ICriticStep` call).

Each run logs progress as it goes - which fixture/repeat is in flight, the verdict, score, elapsed
time and citation count - before the final summary table, so a run that's mid-flight (or stuck) is
visible rather than silent until the end.

Exit code is 0 when every fixture's verdict was both consistent across repeats and matched its
expected answer, 1 otherwise.
