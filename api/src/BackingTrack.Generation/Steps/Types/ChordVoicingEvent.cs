namespace BackingTrack.Generation.Steps.Types;

public record ChordVoicingEvent(double BeatPosition, double DurationInBeats, IReadOnlyList<int> MidiNotes, int Velocity);