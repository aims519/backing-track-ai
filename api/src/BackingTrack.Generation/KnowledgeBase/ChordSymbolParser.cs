using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.KnowledgeBase;

public class ChordSymbolParser
{
    private readonly IReadOnlyList<string> _chordTypes;
    private readonly HashSet<string> _chordNotes = ["A", "B", "C", "D", "E", "F", "G"];
    private readonly HashSet<string> _sharpOrFlat = ["#", "b"];

    public ChordSymbolParser(InMemoryVectorStore inMemoryVectorStore)
    {
        _chordTypes = inMemoryVectorStore.Items
            .Where(e => e.Entry is ChordEntry)
            .Select(x => x.Entry.Id)
            .ToList();
    }

    public IEnumerable<(string root, string quality, bool bValid, string reason)> ValidateChords(IReadOnlyList<string> chords)
    {
        foreach (string chord in chords)
        {
            yield return ValidateChord(chord);
        }
    }

    public (bool bValid, string? root, string reason) ValidateRoot(string? root)
    {
        root = root?.Trim();

        if (string.IsNullOrWhiteSpace(root))
            return (false, root, $"Note has not been provided");

        if (root.Length == 1 && _chordNotes.Contains(root.ToUpper()))
            return (true, root.ToUpper(), string.Empty);

        if (root.Length == 2
           && _chordNotes.Contains(root.Substring(0, 1).ToUpper())
           && _sharpOrFlat.Contains(root.Substring(1, 1).ToLower()))
        {
            return (true, root.Substring(0, 1).ToUpper() + root.Substring(1, 1).ToLower(), string.Empty);
        }

        return (false, root, $"Note {root} is invalid");
    }

    private (string root, string quality, bool bValid, string reason) ValidateChord(string chordString)
    {
        string root = string.Empty;
        string quality = string.Empty;

        if (string.IsNullOrWhiteSpace(chordString))
            return (root, quality, false, "Chord not provided");

        chordString = chordString.Trim();

        root = chordString[..1].ToUpper();
        chordString = chordString[1..];
        if (!_chordNotes.Contains(root))
            return (root, quality, false, $"Chord note {root} is invalid");

        if (chordString.Length == 0)
            return (root, "maj", true, string.Empty);

        string sharpOrFlat = chordString[..1];
        if (_sharpOrFlat.Contains(sharpOrFlat))
        {
            chordString = chordString[1..];
            root += sharpOrFlat;
        }

        quality = chordString.ToLower();
        quality = quality == "" ? "maj" : quality;
        if (!_chordTypes.Contains(quality))
            return (root, quality, false, $"Chord type {quality} is invalid");

        return (root, quality, true, string.Empty);
    }
}