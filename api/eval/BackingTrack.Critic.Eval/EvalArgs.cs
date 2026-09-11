namespace BackingTrack.Critic.Eval;

public sealed class EvalArgs
{
    public int RepeatCount { get; private init; } = 5;
    public IReadOnlyList<string> FixtureIds { get; private init; } = [];
    public bool Verbose { get; private init; }
    public bool ShowHelp { get; private init; }

    public const string HelpText = """
                                    Usage: dotnet run --project api/eval/BackingTrack.Critic.Eval [options]

                                    Runs the real (live) Theory Critic against a hand-authored fixture set to check both
                                    correctness (does the verdict match the fixture's known answer) and consistency (does
                                    the judge agree with itself across repeats). Costs LLM tokens on every run.

                                    Options:
                                      --repeat <n>   Times to review each fixture (default 5).
                                      --id <id>      Only run the fixture with this id. Repeatable.
                                      -v, --verbose  Also log BackingTrack.Generation internals (e.g. LLM request/response detail).
                                      -h, --help     Show this help text.
                                    """;

    public static EvalArgs Parse(string[] args)
    {
        int repeatCount = 5;
        List<string> ids = [];
        bool verbose = false;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    showHelp = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "--repeat" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out repeatCount) || repeatCount < 1)
                        throw new ArgumentException($"--repeat must be a positive integer, got '{args[i]}'.");
                    break;
                case "--id" when i + 1 < args.Length:
                    ids.Add(args[++i]);
                    break;
                default:
                    throw new ArgumentException($"Unrecognised argument: '{args[i]}'.");
            }
        }

        return new EvalArgs { RepeatCount = repeatCount, FixtureIds = ids, Verbose = verbose, ShowHelp = showHelp };
    }
}
