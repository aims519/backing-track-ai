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

    public IEnumerable<(bool bValid, string reason)> ValidateChords(IReadOnlyList<string> chords)
    {
        foreach (string chord in chords)
        {
            yield return ValidateChord(chord);
        }
    }

    private (bool bValid, string reason) ValidateChord(string chord)
    {
        if(string.IsNullOrWhiteSpace(chord))
            return (false, "Chord not provided");
        
        chord = chord.Trim();

        string chordNote = chord[..1].ToUpper();
        chord = chord[1..];
        if(!_chordNotes.Contains(chordNote))
            return (false, $"Chord note {chordNote} is invalid");
        
        if(chord.Length == 0)
            return (true, string.Empty);

        string sharpOrFlat = chord[..1];
        if(_sharpOrFlat.Contains(sharpOrFlat))
            chord = chord[1..];

        string chordType = chord.ToLower();
        chordType = chordType == "" ? "maj" : chordType;
        if (!_chordTypes.Contains(chordType))
            return (false, $"Chord type {chordType} is invalid");
        
        return (true, string.Empty);;
    }
}