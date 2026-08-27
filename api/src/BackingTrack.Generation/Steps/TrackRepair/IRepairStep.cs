using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackRepair;

public interface IRepairStep
{
    Task<CandidateTrack> RepairCandidateTrackAsync(CandidateTrack previousAttempt,
        IReadOnlyList<ValidationFailure> failures, CancellationToken cancellationToken = default);
}