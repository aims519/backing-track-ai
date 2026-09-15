using BackingTrack.Generation.Jobs;
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

    private static readonly string[] SharpPitchClassNames =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private static readonly string[] FlatPitchClassNames =
        ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];

    private static readonly Dictionary<KeyMode, int[]> ModeIntervals = new()
    {
        [KeyMode.Ionian] = [0, 2, 4, 5, 7, 9, 11],
        [KeyMode.Dorian] = [0, 2, 3, 5, 7, 9, 10],
        [KeyMode.Phrygian] = [0, 1, 3, 5, 7, 8, 10],
        [KeyMode.Lydian] = [0, 2, 4, 6, 7, 9, 11],
        [KeyMode.Mixolydian] = [0, 2, 4, 5, 7, 9, 10],
        [KeyMode.Aeolian] = [0, 2, 3, 5, 7, 8, 10],
        [KeyMode.Locrian] = [0, 1, 3, 5, 6, 8, 10],
    };

    public int RootToPitchClass(string root) => PitchClasses.GetValueOrDefault(root, -1);

    /// <summary>
    /// For each chord, finds the knowledge-base scales that both contain the chord's own tones
    /// and are themselves diatonic to the track's key (root &amp; mode).
    /// </summary>
    /// <param name="keyRoot"></param>
    /// <param name="keyMode"></param>
    /// <param name="chords"></param>
    /// <param name="possibleScaleEntries"></param>
    /// <returns>List of matching scales</returns>
    /// <exception cref="ArgumentException"></exception>
    public IReadOnlyList<MatchedScale> FindMatchingScalesForChords(string keyRoot,
        KeyMode keyMode,
        IReadOnlyList<MatchedChord> chords,
        IReadOnlyList<ScaleEntry> possibleScaleEntries)
    {
        int keyPitchClass = RootToPitchClass(keyRoot);
        if (keyPitchClass == -1)
            throw new ArgumentException($"KeyRoot pitch class not found for '{keyRoot}'", nameof(keyRoot));

        int[] keyDiatonicTones = TransposeIntervalsByPitchClass(keyPitchClass, ModeIntervals[keyMode]);

        HashSet<MatchedScale> matchedScalesSet = new(new MatchedScaleComparer());
        foreach (MatchedChord chord in chords)
        {
            int chordPitchClass = RootToPitchClass(chord.Root);
            if (chordPitchClass == -1)
                throw new ArgumentException("Pitch class not found", nameof(chord.Root));

            IEnumerable<ScaleEntry> matchingScaleEntriesForChord = possibleScaleEntries.Where(x =>
                IsScaleValidForChord(chord.ChordEntry.Intervals, x.Intervals)
                && IsScaleValidForChord(TransposeIntervalsByPitchClass(chordPitchClass, x.Intervals), keyDiatonicTones));

            foreach (ScaleEntry scaleEntryForChord in matchingScaleEntriesForChord)
                matchedScalesSet.Add(new(scaleEntryForChord, chord));
        }

        return matchedScalesSet.ToList();
    }

    public int[] TransposeIntervalsByPitchClass(int pitchClass, int[] intervals)
    {
        return intervals.Select(i => (i + pitchClass) % 12).ToArray();
    }

    public bool IsScaleValidForChord(int[] chordTones, int[] scaleTones)
    {
        return chordTones.All(tone => scaleTones.Contains(tone));
    }

    public IReadOnlyList<string> GetChordToneNames(string root, int[] intervals)
    {
        int pitchClass = RootToPitchClass(root);
        if (pitchClass == -1)
            throw new ArgumentException($"Root pitch class not found for '{root}'", nameof(root));

        string[] pitchClassNames = root.Contains('b') ? FlatPitchClassNames : SharpPitchClassNames;

        return TransposeIntervalsByPitchClass(pitchClass, intervals)
            .Select(pc => pitchClassNames[((pc % 12) + 12) % 12])
            .ToList();
    }

    /// <summary>
    /// Given a list of chords and a key, returns a list of any chords whose
    /// notes don't fit
    /// </summary>
    /// <param name="keyRoot"></param>
    /// <param name="keyMode"></param>
    /// <param name="chordsToCheck"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public IReadOnlyList<MatchedChord> GetChordsOutsideKey(string keyRoot, KeyMode keyMode,
        IReadOnlyList<MatchedChord> chordsToCheck)
    {
        HashSet<MatchedChord> chordsOutsideKey = new(chordsToCheck.Count);
        int keyPitchClass = RootToPitchClass(keyRoot);
        if (keyPitchClass == -1)
            throw new ArgumentException($"KeyRoot pitch class not found for '{keyRoot}'", nameof(keyRoot));

        int[] keyDiatonicTones = TransposeIntervalsByPitchClass(keyPitchClass, ModeIntervals[keyMode]);

        foreach (MatchedChord chord in chordsToCheck)
        {
            int chordPitchClass = RootToPitchClass(chord.Root);
            if (chordPitchClass == -1)
                throw new ArgumentException("Pitch class not found", nameof(chord.Root));

            if (!IsScaleValidForChord(TransposeIntervalsByPitchClass(chordPitchClass, chord.ChordEntry.Intervals), keyDiatonicTones))
                chordsOutsideKey.Add(chord);
        }

        return chordsOutsideKey.ToList();
    }

    private sealed class MatchedScaleComparer : IEqualityComparer<MatchedScale>
    {
        public bool Equals(MatchedScale? x, MatchedScale? y) =>
            x?.Chord.ChordEntry.Id == y?.Chord.ChordEntry.Id && x?.Chord.Root == y?.Chord.Root && x?.ScaleEntry.Id == y?.ScaleEntry.Id;

        public int GetHashCode(MatchedScale scale) =>
            HashCode.Combine(scale.Chord.ChordEntry.Id, scale.Chord.Root, scale.ScaleEntry.Id);
    }
}