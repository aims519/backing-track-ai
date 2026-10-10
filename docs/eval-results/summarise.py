#!/usr/bin/env python3
"""Rebuild the results table in docs/eval-results.md from the raw --json reports.

    python3 -I docs/eval-results/summarise.py docs/eval-results/2026-10-09

Reads <run folder>/full-set/<config>-<n>.json (one report per run) and prints the markdown table.
The definitions match the page's "Where each number comes from". The critic eval writes console
text, not JSON, so its numbers are read off the last line of each file in the critic folders and
are not computed here.
"""
import json
import re
import statistics
import sys
from pathlib import Path

# File-name prefix -> "generate / repair / critic" model label. Add a row for each new configuration.
CONFIGS = {
    "baseline": ("gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.6-luna"),
    "sol-luna5": ("gpt-6.1-sol", "gpt-5.6-luna", "gpt-5.6-luna"),
    "sol-luna6": ("gpt-6.1-sol", "gpt-6-luna", "gpt-6-luna"),
    "luna6-all": ("gpt-6-luna", "gpt-6-luna", "gpt-6-luna"),
}
# A rerun of single cases that hit a rate limit: baseline-3b.json or baseline-3.rerun-<case>.json
RERUN = re.compile(r"^(?P<base>.+-\d+)(?:b|\.rerun-.+)\.json$")


def seconds(timespan):
    hours, minutes, secs = timespan.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(secs)


def rate_limited(case):
    return any("TooManyRequests" in n or "Too Many Requests" in n for n in case.get("Notes", []))


def load_runs(folder):
    """{config: [case, ...]} with every run's cases pooled, reruns replacing the case they redo."""
    reports = {}
    for path in sorted(folder.glob("*.json")):
        if not RERUN.match(path.name):
            reports[path.stem] = json.loads(path.read_text())["Cases"]
    for path in sorted(folder.glob("*.json")):
        match = RERUN.match(path.name)
        if match:
            for case in json.loads(path.read_text())["Cases"]:
                reports[match["base"]] = [c for c in reports[match["base"]] if c["Id"] != case["Id"]] + [case]
    pooled = {}
    for name, cases in reports.items():
        pooled.setdefault(name.rsplit("-", 1)[0], []).extend(cases)
    return pooled


def row(config, cases):
    valid = [c for c in cases if not rate_limited(c)]
    if len(valid) != len(cases):
        print(f"warning: {config}: {len(cases) - len(valid)} rate-limited case(s) left out", file=sys.stderr)
    passed = sum(c["Verdict"] == "Pass" for c in valid)
    organic = [c for c in valid if not c["FaultInjected"]]  # no harness fault injection
    fell_back = sum(c["Outcome"] == "SucceededViaFallback" for c in organic)
    # Attempts is always 1 on a fallback, so it only means something where the LLM path succeeded.
    succeeded = [c for c in organic if c["Outcome"] == "Succeeded"]
    times = [seconds(c["Elapsed"]) for c in organic]
    mean = statistics.mean
    label = " / ".join(f"`{m}`" for m in CONFIGS[config]) + " (Flex)"
    return (
        f"| {label} | {passed}/{len(valid)} ({passed / len(valid):.0%}) "
        f"| {mean(c['Attempts'] for c in succeeded):.2f} | {mean(c['CriticAttempts'] for c in succeeded):.2f} "
        f"| {fell_back}/{len(organic)} ({fell_back / len(organic):.0%}) "
        f"| {mean(times):.1f} s ({statistics.median(times):.0f} s) | ${mean(c['CostUsd'] for c in organic):.4f} |"
    )


def main(run_folder):
    runs = load_runs(Path(run_folder) / "full-set")
    print("| Model config (generate / repair / critic) | Pass rate | Mean attempts | Mean critic attempts "
          "| Fallback rate | Mean (median) time per track | Mean cost per track |")
    print("|---|---|---|---|---|---|---|")
    for config in CONFIGS:
        print(row(config, runs[config]))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
