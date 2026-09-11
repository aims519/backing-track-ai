using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Critic.Eval.Fixtures;

/// <summary>
/// One hand-authored candidate track paired with the verdict a competent human reviewer would
/// give it. Unlike the generation harness's golden set, this isn't checking pipeline outcomes —
/// it's checking whether the LLM judge itself agrees with a known-correct verdict, and whether it
/// keeps agreeing with itself across repeated calls.
/// </summary>
public sealed record CriticFixture(
    string Id,
    string Description,
    CandidateTrack CandidateTrack,
    GenerationJob GenerationJob,
    MatchedStyle MatchedStyle,
    IReadOnlyList<MatchedChord> MatchedChords,
    IReadOnlyList<MatchedScale> MatchedScales,
    bool ExpectedPass,
    IReadOnlyList<CriticDimension> ExpectedFailingDimensions);
