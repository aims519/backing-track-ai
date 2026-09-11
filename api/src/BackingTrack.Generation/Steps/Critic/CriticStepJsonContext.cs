using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Steps.Critic;

[JsonSerializable(typeof(CriticCitation))]
[JsonSerializable(typeof(CriticCriterionResult))]
public partial class CriticStepJsonContext : JsonSerializerContext { }