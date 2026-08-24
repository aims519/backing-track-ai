namespace BackingTrack.Generation.Steps.Types;

public record NoteEvent(double BeatPosition, double DurationInBeats, int MidiNote, int Velocity);