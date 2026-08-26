namespace BackingTrack.Generation.Steps.Types;

public record DrumNoteEvent(double BeatPosition, double DurationInBeats, int MidiNote, int Velocity) : NoteEvent(BeatPosition, DurationInBeats, Velocity);