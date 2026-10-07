using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.FallbackGeneration;

namespace BackingTrack.Generation.Workflows.Inputs;

public record FallbackGenerateCandidateTrackInput(GenerationJob GenerationJob, MatchedStyle MatchedStyle, IReadOnlyList<MatchedChord> MatchedChords, IReadOnlyList<MatchedScale> MatchedScales, FallbackReason FallbackReason);