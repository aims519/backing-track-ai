using BackingTrack.Generation.Eval.Model;

namespace BackingTrack.Generation.Eval;

/// <summary>Command-line options for the harness. Hand-parsed to keep the dependency surface small.</summary>
public sealed record EvalHarnessArgs
{
    public IReadOnlyList<EvalCategory> Categories { get; init; } = [];
    public IReadOnlyList<string> CaseIds { get; init; } = [];
    public string? GoldenSetDirectory { get; init; }
    public string? JsonReportPath { get; init; }

    /// <summary>List the cases that match the filters and exit without calling any model.</summary>
    public bool DryRun { get; init; }

    /// <summary>Let the generation pipeline's own logging through at Debug.</summary>
    public bool Verbose { get; init; }

    public bool ShowHelp { get; init; }

    public const string HelpText = """
        BackingTrack.Generation.Eval — end-to-end eval harness for the Precise-mode pipeline.

        Usage: dotnet run -- [options]

          --category <name>   Only run cases in this category (repeatable).
                              Baseline | ValidatorTriggering | Fallback
          --case <id>         Only run this case id (repeatable).
          --golden-set <dir>  Override the golden-set directory (default: ./GoldenSet next to the binary).
          --json <path>       Also write a machine-readable JSON report to <path>.
          --list              List matching cases and exit (no model calls).
          -v, --verbose       Show the generation pipeline's own logs.
          -h, --help          Show this help.

        Exit codes: 0 = pass, 1 = one or more cases failed, 2 = bad usage / no cases matched.
        """;

    public static EvalHarnessArgs Parse(string[] args)
    {
        List<EvalCategory> categories = [];
        List<string> caseIds = [];
        string? goldenSet = null;
        string? json = null;
        bool dryRun = false;
        bool verbose = false;
        bool help = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--category":
                    categories.Add(Enum.Parse<EvalCategory>(RequireValue(args, ++i, "--category"), ignoreCase: true));
                    break;
                case "--case":
                    caseIds.Add(RequireValue(args, ++i, "--case"));
                    break;
                case "--golden-set":
                    goldenSet = RequireValue(args, ++i, "--golden-set");
                    break;
                case "--json":
                    json = RequireValue(args, ++i, "--json");
                    break;
                case "--list":
                    dryRun = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-h" or "--help":
                    help = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }

        return new EvalHarnessArgs
        {
            Categories = categories,
            CaseIds = caseIds,
            GoldenSetDirectory = goldenSet,
            JsonReportPath = json,
            DryRun = dryRun,
            Verbose = verbose,
            ShowHelp = help
        };
    }

    private static string RequireValue(string[] args, int index, string option) =>
        index < args.Length ? args[index] : throw new ArgumentException($"{option} needs a value");
}