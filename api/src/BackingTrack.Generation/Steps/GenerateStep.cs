using System.Text;

using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.TrackGeneration;

namespace BackingTrack.Generation.Steps;

public class GenerateStep(ITrackGenerationClient trackGenerationClient, ScaleMatcher scaleMatcher) : IGenerateStep
{
    public async Task<CandidateTrack> GenerateCandidateTrackAsync(string keyRoot, 
        int tempo,         
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit,
        MatchedStyle style, 
        IReadOnlyList<MatchedChord> chords, 
        IReadOnlyList<MatchedScale> scales,
        CancellationToken cancellationToken = default)
    {
        string chordProgressionString = GenerateChordProgressionStringForPrompt(chords, scales);
        string prompt = GenerateRequestPrompt(keyRoot, tempo, timeSignatureBeatCount, timeSignatureBeatUnit, style, chordProgressionString);
        
        CandidateTrack track = await trackGenerationClient.GenerateTrackAsync(prompt, cancellationToken);

        return track;
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
        int tempo,
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit,
        MatchedStyle style,
        string chordProgression)
    {
        return $"""
               Tempo: {tempo}
               Time Signature: {timeSignatureBeatCount}/{timeSignatureBeatUnit}
               Key Root: {keyRoot}
               Style: {style.StyleEntry.Id} - {style.StyleEntry.Description}
               Chord Progression:
               {chordProgression}
               """;
    }
}