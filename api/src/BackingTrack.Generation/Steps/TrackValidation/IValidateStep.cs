using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackValidation;

public interface IValidateStep
{
    TrackValidationResult ValidateCandidateTrack(CandidateTrack candidateTrack, GenerationJob generationJob, IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales);
}