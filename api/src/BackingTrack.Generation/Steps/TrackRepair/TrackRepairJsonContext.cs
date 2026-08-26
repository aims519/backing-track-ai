using System.Text.Json.Serialization;

using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackRepair;

[JsonSerializable(typeof(ValidationFailure))]
[JsonSerializable(typeof(IReadOnlyList<ValidationFailure>))]
[JsonSerializable(typeof(TrackRepairPatch))]
[JsonDerivedType(typeof(DrumNoteEvent), "drum")]
[JsonDerivedType(typeof(BassNoteEvent), "bass")]
[JsonDerivedType(typeof(ChordVoicingEvent), "chord")]
public partial class TrackRepairJsonContext : JsonSerializerContext { }