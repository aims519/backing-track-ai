using System.Text.Json.Serialization;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

[JsonSerializable(typeof(VectorEntry))]
public partial class VectorStoreJsonContext : JsonSerializerContext { }