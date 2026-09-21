using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Tests.Steps.Critic;

public class CriticStepTests
{
    // Dimension weights are fixed in CriticStep.GetDimensionScoringWeight: ChordToneAdherence = 1,
    // VoiceLeading = 1, RhythmAndFeel = 1, total = 3. Every expected score below is derived from
    // that weighting, not just re-asserting whatever the implementation currently computes.
    private const double ChordToneAdherenceWeight = 1d;
    private const double VoiceLeadingWeight = 1d;
    private const double RhythmAndFeelWeight = 1d;
    private const double TotalWeight = ChordToneAdherenceWeight + VoiceLeadingWeight + RhythmAndFeelWeight;

    private static readonly CandidateTrack DummyTrack = new(16, [], [], []);
    private static readonly MatchedStyle DummyStyle = new(new StyleEntry { Id = "rock", Description = "Rock" });
    private static readonly GenerationJob DummyJob = new()
    {
        Id = Guid.NewGuid(),
        State = JobStates.InProgress,
        StartTime = DateTimeOffset.UtcNow,
        Mode = GenerationMode.Precise,
        Tempo = 120,
        Chords = ["C"],
        KeyRoot = "C",
        KeyMode = KeyMode.Ionian,
        Style = "rock",
        TimeSignatureBeatCount = 4,
        TimeSignatureBeatUnit = 4,
        FeedbackItems = []
    };

    private sealed class StubCriticAiClient(CriticVerdictSubmission submission) : ICriticAiClient
    {
        public Task<CriticVerdictSubmission> GenerateCriticVerdictSubmissionAsync(CandidateTrack candidateTrack,
            GenerationJob generationJob, MatchedStyle matchedStyle, IReadOnlyList<MatchedChord> matchedChords,
            IReadOnlyList<MatchedScale> matchedScales, CancellationToken cancellationToken = default)
            => Task.FromResult(submission);
    }

    private static CriticStep CreateCriticStep(CriticVerdictSubmission submission, double passingScoreThreshold = 50d) =>
        new(new StubCriticAiClient(submission), Options.Create(new CriticSettings { PassingScoreThreshold = passingScoreThreshold, MaxAttempts = 3 }));

    private static Task<CriticFinalReview> ReviewAsync(CriticStep criticStep) =>
        criticStep.ReviewCandidateTrackAsync(DummyTrack, DummyJob, DummyStyle, [], [], TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReviewCandidateTrackAsync_AllDimensionsPass_ScoresFullMarksWithNoCitations()
    {
        CriticVerdictSubmission submission = new([
            new(CriticDimension.ChordToneAdherence, true, null, null),
            new(CriticDimension.VoiceLeading, true, null, null),
            new(CriticDimension.RhythmAndFeel, true, null, null)
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission));

        Assert.True(result.Pass);
        Assert.Equal(100d, result.Score, precision: 8);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_AllDimensionsFail_ScoresZeroWithACitationPerDimension()
    {
        CriticVerdictSubmission submission = new([
            new(CriticDimension.ChordToneAdherence, false, "chord tones ignored", "maj"),
            new(CriticDimension.VoiceLeading, false, "jarring jumps between chords", "blues"),
            new(CriticDimension.RhythmAndFeel, false, "inconsistent feel", "blues")
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission));

        Assert.False(result.Pass);
        Assert.Equal(0d, result.Score, precision: 8);
        Assert.Equal(3, result.Citations.Count);
        Assert.Contains(result.Citations, c =>
            c.Dimension == CriticDimension.ChordToneAdherence && c.Violation == "chord tones ignored" && c.CitedKnowledgeBaseItemId == "maj");
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_MixedPassFail_WeightsScoreByPassingDimensionsOnly()
    {
        // ChordToneAdherence fails; VoiceLeading and RhythmAndFeel pass.
        double expectedScore = (VoiceLeadingWeight + RhythmAndFeelWeight) * (100d / TotalWeight);
        CriticVerdictSubmission submission = new([
            new(CriticDimension.ChordToneAdherence, false, "root notes buried in the voicing", "maj"),
            new(CriticDimension.VoiceLeading, true, null, null),
            new(CriticDimension.RhythmAndFeel, true, null, null)
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission));

        Assert.Equal(expectedScore, result.Score, precision: 8);
        CriticCitation citation = Assert.Single(result.Citations);
        Assert.Equal(CriticDimension.ChordToneAdherence, citation.Dimension);
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_DimensionMissingFromSubmission_FallsBackToWeightedScoreInsteadOfZero()
    {
        // VoiceLeading is never assessed at all; the other two both pass.
        double expectedScore = (ChordToneAdherenceWeight + RhythmAndFeelWeight) * (100d / TotalWeight);
        CriticVerdictSubmission submission = new([
            new(CriticDimension.ChordToneAdherence, true, null, null),
            new(CriticDimension.RhythmAndFeel, true, null, null)
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission));

        Assert.Equal(expectedScore, result.Score, precision: 8);
        Assert.NotEqual(0d, result.Score);
        CriticCitation citation = Assert.Single(result.Citations);
        Assert.Equal(CriticDimension.VoiceLeading, citation.Dimension);
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_MissingDimensionAlongsideARealFailure_CitationListIncludesBoth()
    {
        // VoiceLeading is missing entirely; ChordToneAdherence is assessed and fails; RhythmAndFeel passes.
        CriticVerdictSubmission submission = new([
            new(CriticDimension.ChordToneAdherence, false, "chord tones ignored", "maj"),
            new(CriticDimension.RhythmAndFeel, true, null, null)
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission));

        Assert.Equal(2, result.Citations.Count);
        Assert.Contains(result.Citations, c => c.Dimension == CriticDimension.VoiceLeading);
        Assert.Contains(result.Citations, c =>
            c.Dimension == CriticDimension.ChordToneAdherence && c.Violation == "chord tones ignored");
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_ScoreAtOrAboveThreshold_ReviewPasses()
    {
        // ChordToneAdherence and RhythmAndFeel pass, VoiceLeading fails: score ~= 66.7, threshold 50.
        CriticVerdictSubmission submission = new([
            new(CriticDimension.ChordToneAdherence, true, null, null),
            new(CriticDimension.RhythmAndFeel, true, null, null),
            new(CriticDimension.VoiceLeading, false, "no melodic movement between chords", "blues")
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission, passingScoreThreshold: 50d));

        Assert.True(result.Pass);
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_ScoreBelowThreshold_ReviewFails()
    {
        // Only RhythmAndFeel passes, the rest fail: score ~= 33.3, threshold 50.
        CriticVerdictSubmission submission = new([
            new(CriticDimension.RhythmAndFeel, true, null, null),
            new(CriticDimension.ChordToneAdherence, false, "chord tones ignored", "maj"),
            new(CriticDimension.VoiceLeading, false, "no melodic movement between chords", "blues")
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission, passingScoreThreshold: 50d));

        Assert.False(result.Pass);
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_AssessmentForAnUndefinedDimension_ThrowsArgumentOutOfRangeException()
    {
        // No weight is defined for a CriticDimension outside its three named values - GetDimensionScoringWeight
        // must throw rather than silently scoring it as worthless, per ADR 0029.
        CriticVerdictSubmission submission = new([
            new((CriticDimension)99, true, null, null)
        ]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ReviewAsync(CreateCriticStep(submission)));
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_DuplicatePassingAssessmentsForOneDimension_CountsItsWeightOnce()
    {
        // ChordToneAdherence is (incorrectly) submitted twice, both passing. If the score summed per
        // assessment instead of per unique dimension, this alone would already exceed the fixed
        // 4.5 total weight - the score must never exceed 100 regardless of how many duplicates arrive.
        CriticVerdictSubmission submission = new([
            new(CriticDimension.ChordToneAdherence, true, null, null),
            new(CriticDimension.ChordToneAdherence, true, null, null),
            new(CriticDimension.RhythmAndFeel, true, null, null),
            new(CriticDimension.VoiceLeading, true, null, null)
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission));

        Assert.True(result.Score <= 100d, $"Score {result.Score} exceeds the maximum possible 100 - a duplicate dimension was counted more than once.");
        Assert.Equal(100d, result.Score, precision: 8);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_DuplicateAssessmentsForOneDimensionDisagree_TreatsTheDimensionAsFailed()
    {
        // ChordToneAdherence is submitted twice with conflicting verdicts: one pass, one fail.
        // A dimension only counts as passing if every assessment for it agrees that it passed.
        double expectedScore = (RhythmAndFeelWeight + VoiceLeadingWeight) * (100d / TotalWeight);
        CriticVerdictSubmission submission = new([
            new(CriticDimension.ChordToneAdherence, true, null, null),
            new(CriticDimension.ChordToneAdherence, false, "root notes buried in the voicing", "maj"),
            new(CriticDimension.RhythmAndFeel, true, null, null),
            new(CriticDimension.VoiceLeading, true, null, null)
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission));

        Assert.Equal(expectedScore, result.Score, precision: 8);
        CriticCitation citation = Assert.Single(result.Citations);
        Assert.Equal(CriticDimension.ChordToneAdherence, citation.Dimension);
    }

    [Fact]
    public async Task ReviewCandidateTrackAsync_MultipleFailingAssessmentsForOneDimension_KeepsACitationForEachDistinctViolation()
    {
        // ChordToneAdherence fails twice for two different reasons - both should survive as separate
        // citations rather than one silently overwriting the other, even though the dimension's
        // weight is still only excluded from the score once.
        double expectedScore = (RhythmAndFeelWeight + VoiceLeadingWeight) * (100d / TotalWeight);
        CriticVerdictSubmission submission = new([
            new(CriticDimension.ChordToneAdherence, false, "root notes buried in the voicing", "maj"),
            new(CriticDimension.ChordToneAdherence, false, "leading tone resolves incorrectly", "maj7"),
            new(CriticDimension.RhythmAndFeel, true, null, null),
            new(CriticDimension.VoiceLeading, true, null, null)
        ]);

        CriticFinalReview result = await ReviewAsync(CreateCriticStep(submission));

        Assert.Equal(expectedScore, result.Score, precision: 8);
        Assert.Equal(2, result.Citations.Count);
        Assert.Contains(result.Citations, c => c.Dimension == CriticDimension.ChordToneAdherence && c.Violation == "root notes buried in the voicing");
        Assert.Contains(result.Citations, c => c.Dimension == CriticDimension.ChordToneAdherence && c.Violation == "leading tone resolves incorrectly");
    }
}