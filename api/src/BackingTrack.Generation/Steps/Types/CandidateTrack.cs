namespace BackingTrack.Generation.Steps.Types;

public record CandidateTrack(double LengthInBeats, IReadOnlyList<DrumNoteEvent> Drums, IReadOnlyList<BassNoteEvent> Bass, IReadOnlyList<ChordVoicingEvent> ChordVoicings);