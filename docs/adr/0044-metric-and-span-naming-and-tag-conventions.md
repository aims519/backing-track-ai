# 0044. Metric and span naming, tag values, and what may be a tag

Status: Accepted

## Context

[ADR 0010](0010-opentelemetry-tracing-and-metrics.md) put OpenTelemetry in place; it did not say
how anything emitted through it should be named. Five metrics classes (generation job, track
generation, track repair, critic, push notification) and a job store gauge class were then
added one at a time, plus the platform's own telemetry (ASP.NET Core, HttpClient, Polly). They
settled on the same conventions, but only by copying the previous one, and a few of the
choices were made after a mistake rather than before it.

The reason it matters is that the names and tag values are a contract. Nothing in this repo
commits dashboards or alert rules yet, but they will be built on these series, and in
Prometheus a renamed instrument or tag value is a new series, not a rename: the old chart goes
flat and nothing fails. Tag values are also the one place where the C# compiler gives no help,
because they are strings.

## Decision

**Meters and instruments**

- One meter per area, named `BackingTrack.<Area>` (`BackingTrack.Critic`,
  `BackingTrack.GenerationJob`, …). A class that adds live-state instruments for an area shares
  that area's meter (`JobStoreMetrics` uses the job meter) rather than adding another name to
  register.
- Instrument names are `<area>.<noun>[.<attribute>]` in lowercase snake_case segments joined by
  dots: `track_generation.call.duration`, `critic.calls`, `generation_job.completed`,
  `push_notification.attempts`. Prometheus turns the dots into underscores, adds `_total` to
  counters and the unit to histograms.
- Durations are a `double` histogram with unit `"s"` (so the series ends `_seconds`).
- Events are counters, durations and sizes are histograms, and **current state is an
  observable gauge** (queue depth, jobs per state), which reads the real queue and store when
  collected instead of being updated from every code path that changes them.
- A small, bounded integer (attempts used) also gets a counter tagged by its value, because a
  histogram's default buckets are tuned for millisecond latencies and put every value from 1 to
  N in one bucket.
- A meter exports only if it is registered with `AddMeter` in `OpenTelemetryExtensions`.
  Forgetting it fails silently.

**Tags**

- Keys are lowercase snake_case: `outcome`, `provider`, `model`, `service_tier`, `job_state`,
  `mode`, `status_code`, `reason`. The same concept uses the same key everywhere: a job's state
  is always `job_state`, whichever instrument it is on.
- Calls to an external service are counted in a `*.calls` (or `*.attempts`) counter tagged
  `outcome`, and their duration is recorded for failures as well as successes. A rejected or
  failed attempt that still cost something (tokens, time) is recorded.
- **Values are hard-coded strings, never `ToString()` on an enum.** A rename in C# must not be
  able to change a series. Where a value comes from an enum, a `ToMetricTag()` extension holds
  the mapping (`JobStates`, `GenerationMode`, `OpenAiServiceTier?`), it throws on an unknown
  member, and a test pins every value.
- A value that means "not specified" is its own value (`not_specified`), not folded into the
  provider's default, so it stays distinguishable from an explicit setting.
- The metrics classes take provider-neutral strings (`string serviceTier`), the same as
  `provider` and `model`; the provider-specific mapping stays at the call site.
- **A metric tag must be bounded.** No ids, tokens, URLs or free text. A value that comes from a
  third party is capped, so it cannot create unbounded series: an APNs `reason` longer than 50
  characters is tagged `unknown`. Identifiers such as `job.id` belong on spans and logs, not on
  metrics.
- A gauge that is broken down by a tag reports every value, including zero, so the series falls
  back to 0 instead of going stale.

**Spans**

Span tags are `<area>.<attribute>` (`critic.model`, `track_generation.service_tier`), with the
same hard-coded values as the metrics, and may carry identifiers. Nothing that identifies a
person or device goes in any tag or URL that is exported: the APNs device token is part of the
request path, so it is redacted from the HttpClient span and the built-in HttpClient logging is
removed for that client.

**Use what the platform already emits**

Before adding a counter, check whether a meter we don't export already has it. Voyage retries
are `resilience.polly.strategy.attempt.duration` with `attempt.number` above 0 and the status in
`error.type`, exported by adding `.AddMeter("Polly")`; 503 and 429 responses are already in the
ASP.NET Core request duration series by status code and route.

**Tests**

Each metric is tested with a `MeterListener`, asserting the name, the value and the tags, and
test classes that create the same meter share an xunit collection so listeners don't see each
other's measurements. Gauges are read with `RecordObservableInstruments`.

## Alternatives considered

**Derive tag values from the enum (`state.ToString()`).** Rejected. It is shorter, but renaming
`CompletedViaFallback` would silently start a new series. The explicit mapping costs one switch
per enum and a test.

**Custom counters for things the platform already emits (Polly retries, 503/429).** Rejected.
They would duplicate an existing series and carry less (no status on a retry), and would need
maintaining if the retry policy changed. The cost is that Polly's histograms are in
milliseconds, so those series are `_milliseconds` where ours are `_seconds`.

**Change an established series instead of adding beside it.** Rejected when the job outcome
counter was added: folding it into `generation_job.attempts_used` would have changed that
counter's `outcome` values (`success`/`failed`) under any chart built on it. The two counters
coexist and their totals differ, because `attempts_used` is only recorded when an attempt count
exists; `generation_job.completed` counts every job once.

**Tag an unset service tier as `auto`.** Rejected. With no tier sent, OpenAI falls back to the
project default, which need not be `auto`. `not_specified` stays honest about that.

**Update gauges from the code that changes the queue and store.** Rejected for observable gauges:
a pushed value can drift if one code path is missed, and every path would need to know the
gauge exists.

**Put the device token (or any id) in a tag, or leave it in the URL.** Rejected on privacy and
cardinality grounds.

## Consequences

- A dashboard or alert built on these series is only as stable as the strings. Renaming a tag
  key or value is a breaking change and is treated as one: add the new value, migrate, then
  remove the old one.
- Adding an instrument means: name it by the rules above, register the meter if it is new, pin
  its tag values in a test, and check Prometheus after a real run. A new LLM provider needs its
  own `ToMetricTag()`; the metrics classes don't change.
- Differences that remain, deliberately:
  - Span tag keys (`critic.model`) and metric tag keys (`model`) differ, because a span tag is
    scoped by its prefix and a metric tag by its instrument.
  - The token histograms carry `provider`, `model` and `service_tier` but not `outcome`, so the
    tokens of a failed call that returned a response land in the same series as a successful
    one's. That is right for total spend; an `outcome` tag would split every token series in
    two and can be added if the split is ever wanted.
- **Count counters with "last value minus the last value before the window", not `increase()` or
  `rate()`.** A counter that first appears already at `1` and then stays flat has an `increase()`
  of 0, because there is no earlier sample to subtract. A restart starts every series again (the
  instance id is a label), so on a low-traffic service that restarts on each deploy the first
  event of every series after each restart disappears from `increase()`, and a ratio built from
  two such counters becomes 0/0. The dashboard queries use
  `last_over_time(x[w]) - (last_over_time(x[w] offset w) or 0 * last_over_time(x[w]))`, which
  counts it. The alternative, writing a zero to each counter at startup, only works where the tag
  values are known up front (job outcomes, yes; an APNs `reason`, no), so it is not relied on.
- A queue-depth or job-store gauge is created when its object is, and a DI singleton is lazy,
  so something must depend on it at startup (the eviction service does for `JobStoreMetrics`).
- The queue depth gauge includes jobs cancelled while pending until the worker dequeues them,
  and every instrument describing in-memory state resets on restart.
