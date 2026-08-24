namespace BackingTrack.Generation.Steps.Types;

public record CandidateTrack(double LengthInBeats, IReadOnlyList<NoteEvent> Drums, IReadOnlyList<NoteEvent> Bass, IReadOnlyList<ChordVoicingEvent> ChordVoicings);