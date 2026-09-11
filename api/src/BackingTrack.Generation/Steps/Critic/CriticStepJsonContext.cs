using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Steps.Critic;

[JsonSerializable(typeof(CriticVerdictSubmission))]
public partial class CriticStepJsonContext : JsonSerializerContext { }