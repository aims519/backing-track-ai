using BackingTrack.Generation.Eval.Running;

namespace BackingTrack.Generation.Eval.Reporting;

/// <summary>Renders a <see cref="RunReport"/> as a human-readable summary on stdout.</summary>
public static class ConsoleReporter
{
    public static void Print(RunReport report)
    {
        Console.WriteLine();
        Console.WriteLine($"Eval run — {report.RunAtUtc:u}");
        Console.WriteLine(new string('─', 64));
        Console.WriteLine($"{"Category",-22}{"Pass",6}{"Fail",6}{"Pend",6}{"Total",7}");
        foreach (CategoryTally tally in report.ByCategory)
            Console.WriteLine($"{tally.Category,-22}{tally.Pass,6}{tally.Fail,6}{tally.Pending,6}{tally.Total,7}");
        Console.WriteLine(new string('─', 64));

        List<CaseLine> failed = report.Cases.Where(c => c.Verdict == CaseVerdict.Fail).ToList();
        if (failed.Count > 0)
        {
            Console.WriteLine("\nFailures:");
            foreach (CaseLine line in failed)
                Console.WriteLine($"  ✗ {line.Id,-28} {string.Join("; ", line.Notes)}");
        }

        List<CaseLine> pending = report.Cases.Where(c => c.Verdict == CaseVerdict.Pending).ToList();
        if (pending.Count > 0)
        {
            Console.WriteLine("\nPending (pipeline stage not built yet):");
            foreach (CaseLine line in pending)
                Console.WriteLine($"  · {line.Id,-28} {string.Join("; ", line.Notes)}");
        }

        Console.WriteLine();
        Console.WriteLine($"{report.Passed} passed, {report.Failed} failed, {report.Pending} pending  →  {(report.IsPass ? "PASS" : "FAIL")}");
    }
}