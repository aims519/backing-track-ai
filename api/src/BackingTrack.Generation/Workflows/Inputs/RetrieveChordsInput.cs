using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Workflows.Inputs;

public record RetrieveChordsInput(GenerationJob Job, MatchedStyle MatchedStyle);