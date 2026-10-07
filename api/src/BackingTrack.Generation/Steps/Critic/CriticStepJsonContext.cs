using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Steps.Critic;

[JsonSerializable(typeof(CriticVerdictSubmission))]
[JsonSerializable(typeof(CriticCandidateTrack))]
[JsonSerializable(typeof(CriticGenerationJob))]
[JsonSerializable(typeof(IReadOnlyList<CriticMatchedScale>))]
public partial class CriticStepJsonContext : JsonSerializerContext { }