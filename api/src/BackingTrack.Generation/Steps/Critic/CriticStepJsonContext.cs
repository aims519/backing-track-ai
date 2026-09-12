using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Steps.Critic;

[JsonSerializable(typeof(CriticVerdictSubmission))]
[JsonSerializable(typeof(CriticCandidateTrack))]
public partial class CriticStepJsonContext : JsonSerializerContext { }