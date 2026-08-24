using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Steps.Types;

[JsonSerializable(typeof(NoteEvent))]
[JsonSerializable(typeof(ChordVoicingEvent))]
[JsonSerializable(typeof(CandidateTrack))]
public partial class CandidateTrackJsonContext : JsonSerializerContext { }