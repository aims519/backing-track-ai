using BackingTrack.Generation.Eval.Model;

namespace BackingTrack.Generation.Eval.Running;

/// <summary>
/// Runs a single golden case through the generation pipeline and reports what happened.
/// The seam exists so the same golden set can later be driven black-box over HTTP, or against
/// recorded LLM responses, without changing the evaluator or the reporters.
/// </summary>
public interface IGenerationRunner
{
    Task<GenerationRunResult> RunAsync(EvalCase evalCase, CancellationToken cancellationToken);
}