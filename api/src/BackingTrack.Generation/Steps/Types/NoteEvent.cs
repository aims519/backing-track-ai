using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Steps.Types;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DrumNoteEvent), "drum")]
[JsonDerivedType(typeof(BassNoteEvent), "bass")]
[JsonDerivedType(typeof(ChordVoicingEvent), "chord")]
public abstract record NoteEvent(double BeatPosition, double DurationInBeats, int Velocity);