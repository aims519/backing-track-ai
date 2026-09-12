using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.Critic;

/// <summary>
/// A Wrapper around the base CandidateTrack for the benefit of the Critic, so it doesn't
/// have to do any work to separate the notes into bars itself
/// </summary>
/// <param name="candidateTrack">Base candidate track</param>
/// <param name="beatsPerBar">Number of beats in each bar of the track</param>
public sealed class CriticCandidateTrack(CandidateTrack candidateTrack, int beatsPerBar)
{
    public IReadOnlyList<Bar> Bars => DivideNotesIntoBars();

    private IReadOnlyList<Bar> DivideNotesIntoBars()
    {
        List<Bar> results = [];
        for (int barStartPosition = 0; barStartPosition < candidateTrack.LengthInBeats; barStartPosition += beatsPerBar)
        {
            int barEndPosition = barStartPosition + beatsPerBar;

            IReadOnlyList<DrumNoteEvent> drumNoteEvents = candidateTrack.Drums.Where(n => n.BeatPosition >= barStartPosition
                && n.BeatPosition < barEndPosition).ToArray();

            IReadOnlyList<BassNoteEvent> bassNoteEvents = candidateTrack.Bass.Where(n => n.BeatPosition >= barStartPosition
                && n.BeatPosition < barEndPosition).ToArray();

            IReadOnlyList<ChordVoicingEvent> chordVoicingEvents = candidateTrack.ChordVoicings.Where(n => n.BeatPosition >= barStartPosition
                && n.BeatPosition < barEndPosition).ToArray();

            results.Add(new Bar(drumNoteEvents, bassNoteEvents, chordVoicingEvents));
        }

        return results;
    }
}

public record Bar(IReadOnlyList<DrumNoteEvent> Drums, IReadOnlyList<BassNoteEvent> Bass, IReadOnlyList<ChordVoicingEvent> ChordVoicings) {}