using BackingTrack.Generation.Eval.Model;
using BackingTrack.Generation.Eval.Running;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Eval.Tests.Running;

public class CaseEvaluatorTests
{
    private const int ClosedHiHat = 42;
    private const int Ride = 51;
    private const int Crash = 49;
    private const int Snare = 38;

    [Fact]
    public void Evaluate_EighthNoteHiHatOverMinimum_Passes()
    {
        CandidateTrack track = TrackWithHitsPerBar(bars: 2, hitsPerBar: 8, ClosedHiHat);

        CaseOutcome outcome = CaseEvaluator.Evaluate(CaseWithTimekeepingMinimum(6), Succeeded(track));

        Assert.Equal(CaseVerdict.Pass, outcome.Verdict);
    }

    [Fact]
    public void Evaluate_QuarterNoteHiHatUnderMinimum_FailsWithAverageInNote()
    {
        CandidateTrack track = TrackWithHitsPerBar(bars: 2, hitsPerBar: 4, ClosedHiHat);

        CaseOutcome outcome = CaseEvaluator.Evaluate(CaseWithTimekeepingMinimum(6), Succeeded(track));

        Assert.Equal(CaseVerdict.Fail, outcome.Verdict);
        string note = Assert.Single(outcome.Notes);
        Assert.Contains("got 4", note);
    }

    [Fact]
    public void Evaluate_NoTimekeepingMinimumSet_IgnoresSparseDrums()
    {
        CandidateTrack track = new(8, [], [], []);

        CaseOutcome outcome = CaseEvaluator.Evaluate(CaseWithTimekeepingMinimum(null), Succeeded(track));

        Assert.Equal(CaseVerdict.Pass, outcome.Verdict);
    }

    [Fact]
    public void Evaluate_GenerationFailedWithNoTrack_ReportsOnlyTheOutcome()
    {
        GenerationRunResult failed = new() { Outcome = ActualOutcome.Failed, Elapsed = TimeSpan.Zero };

        CaseOutcome outcome = CaseEvaluator.Evaluate(CaseWithTimekeepingMinimum(6), failed);

        string note = Assert.Single(outcome.Notes);
        Assert.Contains("expected outcome", note);
    }

    [Fact]
    public void AverageTimekeepingHitsPerBar_FillBarWithNoHiHat_IsAveragedNotFailedOutright()
    {
        // Bar 1: eight hats. Bar 2: a snare fill, no hats.
        IReadOnlyList<DrumNoteEvent> drums =
        [
            .. Hits(barStart: 0, count: 8, ClosedHiHat),
            .. Hits(barStart: 4, count: 8, Snare)
        ];
        CandidateTrack track = new(8, drums, [], []);

        double average = CaseEvaluator.AverageTimekeepingHitsPerBar(track, beatsPerBar: 4);

        Assert.Equal(4, average);
    }

    [Fact]
    public void AverageTimekeepingHitsPerBar_CountsRideButNotCrash()
    {
        IReadOnlyList<DrumNoteEvent> drums =
        [
            .. Hits(barStart: 0, count: 4, Ride),
            .. Hits(barStart: 0, count: 4, Crash)
        ];
        CandidateTrack track = new(4, drums, [], []);

        double average = CaseEvaluator.AverageTimekeepingHitsPerBar(track, beatsPerBar: 4);

        Assert.Equal(4, average);
    }

    [Fact]
    public void AverageTimekeepingHitsPerBar_EmptyTrack_IsZero()
    {
        CandidateTrack track = new(0, [], [], []);

        double average = CaseEvaluator.AverageTimekeepingHitsPerBar(track, beatsPerBar: 4);

        Assert.Equal(0, average);
    }

    private static EvalCase CaseWithTimekeepingMinimum(double? minimum) => new()
    {
        Id = "test-case",
        Category = EvalCategory.Baseline,
        Description = "test",
        Input = new GenerationInput { Tempo = 120, Chords = ["E", "A"], KeyRoot = "E", Style = "rock" },
        Expect = new CaseExpectations
        {
            Outcome = ExpectedOutcome.Succeeds,
            MinAverageTimekeepingHitsPerBar = minimum
        }
    };

    private static GenerationRunResult Succeeded(CandidateTrack track) => new()
    {
        Outcome = ActualOutcome.Succeeded,
        Attempts = 1,
        CriticAttempts = 1,
        Track = track,
        Elapsed = TimeSpan.Zero
    };

    private static CandidateTrack TrackWithHitsPerBar(int bars, int hitsPerBar, int midiNote)
    {
        List<DrumNoteEvent> drums = [];
        for (int bar = 0; bar < bars; bar++)
            drums.AddRange(Hits(barStart: bar * 4, count: hitsPerBar, midiNote));

        return new CandidateTrack(bars * 4, drums, [], []);
    }

    private static IEnumerable<DrumNoteEvent> Hits(int barStart, int count, int midiNote)
    {
        double spacing = 4.0 / count;
        return Enumerable.Range(0, count)
            .Select(i => new DrumNoteEvent(barStart + i * spacing, spacing, midiNote, Velocity: 80));
    }
}