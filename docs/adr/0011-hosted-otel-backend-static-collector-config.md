# 0011. Hosted Grafana Cloud backend, static OTel Collector config on the Pi

Status: Accepted

## Context

ADR 0010 added OpenTelemetry tracing/metrics/logging to the API and verified it locally against
a Docker Compose collector + `grafana/otel-lgtm` stack. The next step was giving the Pi
deployment somewhere real to send that data, raising two separate questions: where does the
observability *backend* (Tempo/Loki/Prometheus equivalent) actually run, and how is the
Pi-side *collector* itself configured and managed.

## Decision

**Backend: Grafana Cloud's free tier**, not self-hosted. The Pi runs only a lightweight
`otelcol-contrib` collector (`scripts/pi-otel-bootstrap.sh`, `scripts/otel-collector/`),
exporting via `otlphttp` + the `basicauth` extension to Grafana Cloud's OTLP gateway. No Tempo,
Loki, or Prometheus containers run on the Pi.

**Collector management: a static, git-tracked YAML config**, not Grafana's OpAMP
supervisor/Fleet Management. The collector's config lives in the repo and is deployed manually
(same one-time-bootstrap pattern as `pi-bootstrap.sh`), not pulled at runtime from Grafana
Cloud's dashboard.

The collector runs as its own systemd service (`otel-collector.service`) under its own system
user (`otel-collector`), rooted at `/opt/otel-collector/` — deliberately *not* nested under
`/opt/backingtrack/` (BackingTrack.Api's own root), because it's Pi-wide infrastructure: any
future project on this Pi could send OTLP data to the same local collector, the same way
`ci-deploy` (per `pi-bootstrap.sh`'s own comments) is a shared deploy user rather than a
per-project one.

The API's OTLP exporters point at `localhost:4317` in `appsettings.Production.json` — the app
talks to the local collector, not directly to Grafana Cloud, keeping the same
app-to-local-collector shape as the local dev setup in ADR 0010.

## Alternatives considered

**Self-hosting the backend on the Pi** (the same `grafana/otel-lgtm` stack used for local dev,
run either on the Pi itself or another always-on device on the network) — rejected. The Pi is
the same resource-constrained device already being kept deliberately lean for the "edge-deployed
on constrained hardware" portfolio differentiator (ADR 0008); running Tempo/Loki/Prometheus
alongside the actual API workload competes for the same limited CPU/memory the AOT decision was
made to conserve. Running it on a second device was also considered, but adds a device
dependency this project doesn't otherwise have, for a hobby-scale, single-user API.

**Grafana's OpAMP supervisor / Fleet Management** — rejected. It lets Grafana Cloud push
collector config remotely rather than deploying a static file, which is valuable when managing
many collectors across many machines centrally. This project has one Pi. Adopting it would add
a live runtime dependency on Grafana Cloud's control plane just to run a local collector, plus
an extra supervisor process, and would move collector config out of git into their UI —
contrary to how every other deployable piece of this project (the API itself, this collector
config) is version-controlled and deployed the same way.

## Consequences

Telemetry now depends on Grafana Cloud's free-tier availability and quota — if usage grows
beyond free-tier limits, or Grafana Cloud has an outage, tracing/metrics/logging degrade or stop
without affecting the API itself (the collector fails to export, but nothing in the app's
request path blocks on it). This is an accepted tradeoff for a hobby-scale project, not
something actively monitored for yet.

Updating the collector config (e.g. adding a new pipeline, bumping `otelcol-contrib`'s version)
requires the same manual re-copy-and-rerun process as `pi-bootstrap.sh` — there's no CI/CD path
for the collector the way there is for the API's own deploys (`.github/workflows/deploy.yml`).
If collector config changes become frequent, that manual step is worth automating into the same
pipeline; it hasn't been yet because this is the first deploy.

The Grafana Cloud instance ID (`1745886`) and OTLP endpoint region (`prod-gb-south-1`) are
hardcoded directly in `scripts/otel-collector/otel-collector-config.yaml` — this is deliberate,
since neither is a secret (the instance ID is a public-facing account identifier, the endpoint
is a fixed regional URL), unlike the API token, which lives only in `/opt/otel-collector/otel-
collector.env` on the Pi and is never committed.
