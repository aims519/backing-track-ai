using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.TrackGeneration;

namespace BackingTrack.Generation.Tests.Steps;

public class GenerateStepTests
{
    // Not exercised by any test here - GenerateChordProgressionStringForPrompt never calls it.
    private class NotUsedTrackGenerationClient : ITrackGenerationClient
    {
        public Task<CandidateTrack> GenerateTrackAsync(string prompt, CancellationToken token = default)
            => throw new NotSupportedException("Not expected to be called by these tests");
    }

    private static GenerateStep CreateGenerateStep() => new(new NotUsedTrackGenerationClient(), new ScaleMatcher());

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