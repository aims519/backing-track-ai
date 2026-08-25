using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackValidation;

public interface ITrackValidator
{
    IReadOnlyList<ValidationFailure> ValidateLengthInBeats(CandidateTrack candidateTrack, GenerationJob generationJob);
    IReadOnlyList<ValidationFailure> ValidateDrumNoteChoices(IReadOnlyList<DrumNoteEvent> drumNoteEvents);

    IReadOnlyList<ValidationFailure> ValidateBassNoteChoices(IReadOnlyList<BassNoteEvent> bassNoteEvents,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales,
        GenerationJob generationJob);
}