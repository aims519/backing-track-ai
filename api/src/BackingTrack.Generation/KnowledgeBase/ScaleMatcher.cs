using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.KnowledgeBase;

public class ScaleMatcher
{
    private static readonly Dictionary<string, int> PitchClasses = new()
    {
        ["C"] = 0,
        ["C#"] = 1,
        ["Db"] = 1,
        ["D"] = 2,
        ["D#"] = 3,
        ["Eb"] = 3,
        ["E"] = 4,
        ["Fb"] = 4,
        ["E#"] = 5,
        ["F"] = 5,
        ["F#"] = 6,
        ["Gb"] = 6,
        ["G"] = 7,
        ["G#"] = 8,
        ["Ab"] = 8,
        ["A"] = 9,
        ["A#"] = 10,
        ["Bb"] = 10,
        ["B"] = 11,
        ["Cb"] = 11,
        ["B#"] = 0,
    };

    public int RootToPitchClass(string root) => PitchClasses.GetValueOrDefault(root, -1);

    public IReadOnlyList<ScaleEntry> FindMatchingScales(string key,
        IReadOnlyList<MatchedChord> chords,
        IReadOnlyList<ScaleEntry> possibleScaleEntries)
    {
        int keyPitchClass = RootToPitchClass(key);
        if(keyPitchClass == -1)
            throw new ArgumentException($"Key pitch class not found for '{key}'", nameof(key));
        
        HashSet<ScaleEntry> allMatchingScales = possibleScaleEntries.ToHashSet();
        
        foreach (MatchedChord chord in chords)
        {
            int chordPitchClass = RootToPitchClass(chord.Root);
            if(chordPitchClass == -1)
                throw new ArgumentException("Pitch class not found", nameof(chord.Root));

            int[] adjustedChordIntervals = TransposeIntervalsByPitchClass(chordPitchClass, chord.ChordEntry.Intervals);
            
            IEnumerable<ScaleEntry> matchingScaleEntriesForChord = possibleScaleEntries.Where(x => IsScaleValidForChord(adjustedChordIntervals,
                TransposeIntervalsByPitchClass(keyPitchClass, x.Intervals)));

            allMatchingScales = allMatchingScales.Intersect(matchingScaleEntriesForChord).ToHashSet();
        }
        
        return allMatchingScales.ToList();
    }
    
    public int[] TransposeIntervalsByPitchClass(int pitchClass, int[] intervals)
    {
        return intervals.Select(i => (i + pitchClass) % 12).ToArray();
    }

    public bool IsScaleValidForChord(int[] chordTones, int[] scaleTones)
    {
        return chordTones.All(tone => scaleTones.Contains(tone));
    }
}