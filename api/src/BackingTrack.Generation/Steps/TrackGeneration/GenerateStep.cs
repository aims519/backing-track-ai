using System.Text;
using System.Text.Json;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackGeneration;

public class GenerateStep(ITrackGenerationClient trackGenerationClient, ScaleMatcher scaleMatcher) : IGenerateStep
{
    public async Task<CandidateTrack> GenerateCandidateTrackAsync(string keyRoot,
        KeyMode keyMode,
        int tempo,
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit,
        MatchedStyle style,
        IReadOnlyList<MatchedChord> chords,
        IReadOnlyList<MatchedScale> scales,
        CandidateTrack? previousAttempt = null,
        IReadOnlyList<ValidationFailure>? previousAttemptFailures = null,
        CancellationToken cancellationToken = default)
    {
        string chordProgressionString = GenerateChordProgressionStringForPrompt(chords, scales);
        string prompt = GenerateRequestPrompt(keyRoot, keyMode, tempo, timeSignatureBeatCount, timeSignatureBeatUnit, style, chordProgressionString, chords.Count);

        if (previousAttempt != null && previousAttemptFailures != null)
            prompt += ("\n" + GeneratePreviousFailurePrompt(previousAttempt, previousAttemptFailures));

        CandidateTrack track = await trackGenerationClient.GenerateTrackAsync(prompt, cancellationToken);

        // Length is derived from the request, not generated - the prompt tells the model to send 0.
        return track with { LengthInBeats = timeSignatureBeatCount * chords.Count };
    }

    public async Task<CandidateTrack> GenerateCandidateTrackWithCriticReviewAsync(string keyRoot, KeyMode keyMode, int tempo, int timeSignatureBeatCount,
        int timeSignatureBeatUnit, MatchedStyle style, IReadOnlyList<MatchedChord> chords, IReadOnlyList<MatchedScale> scales,
        CandidateTrack previousAttempt, CriticFinalReview criticReview, CancellationToken cancellationToken = default)
    {
        string chordProgressionString = GenerateChordProgressionStringForPrompt(chords, scales);
        string prompt = GenerateRequestPrompt(keyRoot, keyMode, tempo, timeSignatureBeatCount, timeSignatureBeatUnit, style, chordProgressionString, chords.Count);
        string criticReviewString = GenerateCriticReviewPrompt(previousAttempt, criticReview);

        prompt += ("\n" + criticReviewString);

        CandidateTrack track = await trackGenerationClient.GenerateTrackAsync(prompt, cancellationToken);

        // Length is derived from the request, not generated - the prompt tells the model to send 0.
        return track with { LengthInBeats = timeSignatureBeatCount * chords.Count };
    }

    internal string GenerateChordProgressionStringForPrompt(IReadOnlyList<MatchedChord> chords,
        IReadOnlyList<MatchedScale> scales)
    {
        StringBuilder builder = new();
        foreach (MatchedChord chord in chords)
        {
            IReadOnlyList<MatchedScale> scalesForChord = scales.Where(s => s.Chord == chord).ToList();
            string scaleString = $"{string.Join(',', scalesForChord.Select(s => $"{s.ScaleEntry.Id} [{string.Join(',', s.ScaleEntry.Intervals)}]"))}";

            IReadOnlyList<string> chordTones = scaleMatcher.GetChordToneNames(chord.Root, chord.ChordEntry.Intervals);
            string chordToneString = $"[{string.Join(',', chordTones)}]";

            builder.AppendLine($"{chord.Root} {chord.ChordEntry.Id} {chordToneString}. Scales: {scaleString}");
        }

        return builder.ToString();
    }

    private string GenerateRequestPrompt(string keyRoot,
        KeyMode keyMode,
        int tempo,
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit,
        MatchedStyle style,
        string chordProgression,
        int barCount)
    {
        int totalBeats = timeSignatureBeatCount * barCount;

        return $"""
               Tempo: {tempo}
               Time Signature: {timeSignatureBeatCount}/{timeSignatureBeatUnit}
               Key Root: {keyRoot}
               Key Mode: {keyMode}
               Style: {style.StyleEntry.Id} - {style.StyleEntry.Description}
               Chord Progression:
               {chordProgression}
               Total length: {totalBeats} beats across {barCount} bars ({timeSignatureBeatCount} beats each), beat
               positions 0 to {totalBeats}. Every bar in that range must contain drum, bass, and chord events -
               do not stop generating after the first one or two bars.
               """;
    }

    private string GeneratePreviousFailurePrompt(CandidateTrack previousAttempt,
        IReadOnlyList<ValidationFailure> previousAttemptFailures)
    {
        return $"""
               ## Critic Review
               Your previous attempt at generating a track failed. Here are the failure messages:
               {string.Join('\n', previousAttemptFailures.Select(f => f.Reason))}
               
               And the full output that was produced:
               {JsonSerializer.Serialize(previousAttempt, CandidateTrackJsonContext.Default.CandidateTrack)}
               """;
    }

    private string GenerateCriticReviewPrompt(CandidateTrack previousAttempt,
        CriticFinalReview criticReview)
    {
        return $"""
                Your previous attempt at generating a track did not pass the music critic review. Here are the issues found:
                {string.Join('\n', criticReview.Citations.Select(f => f.Violation))}

                And the full output that was produced:
                {JsonSerializer.Serialize(previousAttempt, CandidateTrackJsonContext.Default.CandidateTrack)}
                """;
    }
}