namespace BackingTrack.Generation.Steps.Types;

public record BassNoteEvent(double BeatPosition, double DurationInBeats, int MidiNote, int Velocity);