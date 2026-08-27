using Anthropic.Models.Beta.Deployments;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Agents.AI;

namespace BackingTrack.Generation.Steps.TrackValidation;

public interface ITrackValidator
{
    IReadOnlyList<ValidationFailure> ValidateLengthInBeats(CandidateTrack candidateTrack, GenerationJob generationJob);
    IReadOnlyList<ValidationFailure> ValidateDrumNoteChoices(IReadOnlyList<DrumNoteEvent> drumNoteEvents);

    IReadOnlyList<ValidationFailure> ValidateBassNoteChoices(IReadOnlyList<BassNoteEvent> bassNoteEvents,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales,
        GenerationJob generationJob);
    
    IReadOnlyList<ValidationFailure> ValidateChordVoicingNoteChoices(IReadOnlyList<ChordVoicingEvent> chordVoicingEvents,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales,
        GenerationJob generationJob);
    
    IReadOnlyList<ValidationFailure> ValidateMidiVelocities(CandidateTrack candidateTrack);
    
    IReadOnlyList<ValidationFailure> ValidateNoPartEmpty(CandidateTrack candidateTrack);
    
    IReadOnlyList<ValidationFailure> ValidateBeatPositionsAndDurations(CandidateTrack candidateTrack, GenerationJob generationJob);
}