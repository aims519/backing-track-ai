using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackGeneration;

public interface ITrackGenerationClient
{
    Task<CandidateTrack> GenerateTrackAsync(string prompt, CancellationToken token = default);
}