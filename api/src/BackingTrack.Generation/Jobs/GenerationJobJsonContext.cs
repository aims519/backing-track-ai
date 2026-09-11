using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Jobs;

[JsonSerializable(typeof(GenerationJob))]
public partial class GenerationJobJsonContext : JsonSerializerContext { }