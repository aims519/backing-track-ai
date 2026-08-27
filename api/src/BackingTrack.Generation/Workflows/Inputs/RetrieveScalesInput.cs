using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Workflows.Inputs;

public record RetrieveScalesInput(IReadOnlyList<MatchedChord> MatchedChords, MatchedStyle MatchedStyle, GenerationJob GenerationJob);