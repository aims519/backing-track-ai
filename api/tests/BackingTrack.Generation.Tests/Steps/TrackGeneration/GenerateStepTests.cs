using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Tests.Steps.TrackGeneration;

public class GenerateStepTests
{
    // Not exercised by the GenerateChordProgressionStringForPrompt tests - that method never calls it.
    private class NotUsedTrackGenerationClient : ITrackGenerationClient
    {
        public Task<CandidateTrack> GenerateTrackAsync(string prompt, CancellationToken token = default)
            => throw new NotSupportedException("Not expected to be called by these tests");
    }

    private sealed class StubTrackGenerationClient(CandidateTrack track) : ITrackGenerationClient
    {
        public Task<CandidateTrack> GenerateTrackAsync(string prompt, CancellationToken token = default)
            => Task.FromResult(track);
    }

    private static GenerateStep CreateGenerateStep() => new(new NotUsedTrackGenerationClient(), new ScaleMatcher());

    [Fact]
    public async Task GenerateCandidateTrackAsync_OverwritesModelLengthInBeatsWithBeatsPerBarTimesChordCount()
    {
        // The prompt tells the model to send 0; the system owns track length, not the model.
        CandidateTrack modelOutput = new(LengthInBeats: 999, Drums: [], Bass: [], ChordVoicings: []);
        GenerateStep generateStep = new(new StubTrackGenerationClient(modelOutput), new ScaleMatcher());

        ChordEntry majChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };
        MatchedChord[] chords =
            [new(majChordEntry, "C"), new(majChordEntry, "G"), new(majChordEntry, "F"), new(majChordEntry, "G")];
        MatchedStyle style = new(new StyleEntry { Id = "rock", Description = "Rock" });

        CandidateTrack result = await generateStep.GenerateCandidateTrackAsync(
            keyRoot: "C", tempo: 120, timeSignatureBeatCount: 4, timeSignatureBeatUnit: 4,
            style: style, chords: chords, scales: [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(16, result.LengthInBeats);
    }

    [Fact]
    public void GenerateChordProgressionStringForPrompt_SingleChordSingleScale_FormatsRootQualityAndScaleWithIntervals()
    {
        GenerateStep generateStep = CreateGenerateStep();

        ChordEntry majChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };
        MatchedChord chord = new(majChordEntry, "C");

        ScaleEntry majorScaleEntry = new() { Id = "major", Intervals = [0, 2, 4, 5, 7, 9, 11], Description = "Major scale" };
        MatchedScale scale = new(majorScaleEntry, chord);

        string result = generateStep.GenerateChordProgressionStringForPrompt([chord], [scale]);

        Assert.Equal($"C maj [C,E,G]. Scales: major [0,2,4,5,7,9,11]{Environment.NewLine}", result);
    }

    [Fact]
    public void GenerateChordProgressionStringForPrompt_ChordWithMultipleScales_JoinsScalesWithComma()
    {
        GenerateStep generateStep = CreateGenerateStep();

        ChordEntry majChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };
        MatchedChord chord = new(majChordEntry, "C");

        ScaleEntry majorScaleEntry = new() { Id = "major", Intervals = [0, 2, 4, 5, 7, 9, 11], Description = "Major scale" };
        ScaleEntry mixolydianScaleEntry = new() { Id = "mixolydian", Intervals = [0, 2, 4, 5, 7, 9, 10], Description = "Mixolydian mode" };

        string result = generateStep.GenerateChordProgressionStringForPrompt(
            [chord],
            [new MatchedScale(majorScaleEntry, chord), new MatchedScale(mixolydianScaleEntry, chord)]);

        Assert.Equal($"C maj [C,E,G]. Scales: major [0,2,4,5,7,9,11],mixolydian [0,2,4,5,7,9,10]{Environment.NewLine}", result);
    }

    [Fact]
    public void GenerateChordProgressionStringForPrompt_MultipleChords_PreservesProgressionOrderAndAssociatesOwnScalesOnly()
    {
        GenerateStep generateStep = CreateGenerateStep();

        ChordEntry majChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };
        ChordEntry minChordEntry = new() { Id = "min", Intervals = [0, 3, 7], Description = "Minor triad" };

        MatchedChord cMajor = new(majChordEntry, "C");
        MatchedChord aMinor = new(minChordEntry, "A");

        ScaleEntry majorScaleEntry = new() { Id = "major", Intervals = [0, 2, 4, 5, 7, 9, 11], Description = "Major scale" };
        ScaleEntry naturalMinorScaleEntry = new() { Id = "minor", Intervals = [0, 2, 3, 5, 7, 8, 10], Description = "Natural minor scale" };

        string result = generateStep.GenerateChordProgressionStringForPrompt(
            [cMajor, aMinor],
            [new MatchedScale(majorScaleEntry, cMajor), new MatchedScale(naturalMinorScaleEntry, aMinor)]);

        string expected = $"C maj [C,E,G]. Scales: major [0,2,4,5,7,9,11]{Environment.NewLine}" +
                           $"A min [A,C,E]. Scales: minor [0,2,3,5,7,8,10]{Environment.NewLine}";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GenerateChordProgressionStringForPrompt_ChordWithNoMatchingScales_LeavesScalesSegmentEmpty()
    {
        GenerateStep generateStep = CreateGenerateStep();

        ChordEntry majChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };
        MatchedChord chord = new(majChordEntry, "C");

        string result = generateStep.GenerateChordProgressionStringForPrompt([chord], []);

        Assert.Equal($"C maj [C,E,G]. Scales: {Environment.NewLine}", result);
    }
}