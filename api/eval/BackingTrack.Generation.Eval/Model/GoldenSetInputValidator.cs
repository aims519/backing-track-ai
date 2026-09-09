using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;

namespace BackingTrack.Generation.Eval.Model;

/// <summary>
/// Checks every case's input the way the API's request validation would before it reaches the
/// pipeline. The harness bypasses the <c>/generate</c> endpoint, so without this a malformed
/// case — a bad chord symbol, say — degrades silently (retrieval just drops the chord) instead
/// of being rejected, and you burn a run chasing a phantom pipeline bug.
/// </summary>
public static class GoldenSetInputValidator
{
    private static readonly IReadOnlySet<int> ValidBeatUnits = new HashSet<int> { 2, 4, 8, 16, 32 };

    public static IReadOnlyList<string> Validate(IReadOnlyList<EvalCase> cases, ChordSymbolParser chordSymbolParser)
    {
        List<string> errors = [];

        foreach (EvalCase evalCase in cases)
        {
            GenerationInput input = evalCase.Input;

            if (!Enum.TryParse<GenerationMode>(input.Mode, out _))
                errors.Add($"{evalCase.Id}: unknown mode '{input.Mode}'");

            if (input.Tempo is < 40 or > 240)
                errors.Add($"{evalCase.Id}: tempo {input.Tempo} is outside 40-240");

            if (input.Chords.Count == 0)
                errors.Add($"{evalCase.Id}: no chords");

            List<(string Root, string Quality, bool IsValid, string Reason)> parsedChords =
                chordSymbolParser.ValidateChords(input.Chords).ToList();
            for (int i = 0; i < parsedChords.Count; i++)
            {
                if (!parsedChords[i].IsValid)
                    errors.Add($"{evalCase.Id}: chord[{i}] '{input.Chords[i]}' is invalid - {parsedChords[i].Reason}");
            }

            if (!chordSymbolParser.ValidateRoot(input.KeyRoot).isValid)
                errors.Add($"{evalCase.Id}: keyRoot '{input.KeyRoot}' is not a valid root note");

            if (input.TimeSignatureBeatCount is < 2 or > 32)
                errors.Add($"{evalCase.Id}: timeSignatureBeatCount {input.TimeSignatureBeatCount} is outside 2-32");

            if (!ValidBeatUnits.Contains(input.TimeSignatureBeatUnit))
                errors.Add($"{evalCase.Id}: timeSignatureBeatUnit {input.TimeSignatureBeatUnit} is not one of 2, 4, 8, 16, 32");

            if (string.IsNullOrWhiteSpace(input.Style))
                errors.Add($"{evalCase.Id}: no style");
        }

        return errors;
    }
}