namespace BackingTrack.Generation.Eval.Model;

/// <summary>
/// Which part of the generation pipeline a golden case is written to exercise. The category
/// drives how a result is judged and, when a run regresses, where to look first.
/// </summary>
public enum EvalCategory
{
    /// <summary>Clean input the pipeline should satisfy on the first attempt — no repair, no fallback.</summary>
    Baseline,

    /// <summary>
    /// Input expected to fail deterministic validation on the first candidate, exercising the
    /// validate → repair → re-validate loop.
    /// </summary>
    ValidatorTriggering,

    /// <summary>
    /// Input the pipeline cannot satisfy within the repair budget, so it must land on the
    /// deterministic rule-based fallback generator.
    /// </summary>
    Fallback,

    /// <summary>
    /// Input expected to pass deterministic validation but draw at least one Theory Critic
    /// rejection, exercising the critic → regenerate → re-review loop.
    /// </summary>
    CriticTriggering
}