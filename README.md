# backing-track-ai

A Raspberry Pi-hosted .NET application that uses an agentic AI workflow to generate musical backing tracks for guitar practice, with validation, repair and a rule-based fallback guarding against bad output. The iOS client app allows for customisable tracks to be generated for a chosen chord progression, tempo, and style. The project was developed largely as an exercise in AI-centric engineering, but also to be practically useful to the author.

For the full project scope, please see the [docs/backing-track-generator-scope.md](docs/backing-track-generator-scope.md)

## Overview

### Core flow

1. User selects a key, style, chord progression, time signature and tempo
2. App sends a request to the backend at `POST /tracks/generate`. The API returns a `202` response along with a unique job ID
3. User can choose to watch the progress of their generating track, which includes messages published from the API about what stage of the process is active, and any validation failures/critic rejections. Updates are pulled by the app using `GET /tracks/status/{jobId}`
4. Alternatively the user can dismiss the progress screen and carry on. The user is notified via APNs push notifications when a track is ready
5. Once the track is complete, `GET /tracks/download/{trackId}` downloads a JSON file that describes the notes of the track
6. The `AVFAudio`-powered audio engine converts the JSON into a playable sequence of notes, which are then played using sampled instruments
7. During playback, which is fully offline, the track tempo can be adjusted as needed, as well as the volumes of any of the tracks
8. All downloaded tracks are stored locally in-app using SwiftData and can be replayed from a history screen at any time

### Backend

Backing tracks are generated asynchronously via an in-memory queue, using a multi-step workflow (see diagram below).

The results of the workflow are not audio files, but JSON files containing definitions of MIDI notes, with three tracks (bass, drums, and chords). Client applications are responsible for playing the tracks after downloading these JSON files.

The application is self-hosted on a Raspberry Pi, reachable from a private Tailscale network.

### Client

The client is a native iOS application built using SwiftUI. The app includes an audio engine (built using Apple's AVFAudio libraries), which uses sampled instruments and sequencers to play the tracks. A history of previously generated tracks is kept to be replayed without the need of a network connection.

## Architecture

### System

![System architecture: the iOS app talks to the ASP.NET Core API on a Raspberry Pi over Tailscale; the API calls OpenAI, Voyage AI, APNs and Grafana Cloud; GitHub Actions deploys to the Pi and uploads builds to TestFlight](docs/images/system-architecture.svg)

The iOS app talks to the ASP.NET Core API on a Raspberry Pi over a private Tailscale network, to avoid exposing it to the public internet. GitHub Actions deploys the API to the Pi and uploads iOS builds to TestFlight. The Pi also holds a set of pre-computed vector files, loaded into memory for use in style context retrieval, as well as the output JSON files for each track.

Jobs themselves are held in memory and processed one at a time in a queue. The in-memory storage means that jobs mid-run can be lost on restart of the API, but this is acceptable for the time being. A future enhancement could be to store jobs in a database such as PostgreSQL instead. The following external services are also used:

- APNs: push notifications are sent on completion of backing tracks
- OpenAI: LLM calls are used in the Generate, Repair, and Critic steps discussed further below
- Voyage AI: used for text vector embedding in the context retrieval stage. Currently only the style information is retrieved using embedding
- Grafana Cloud: telemetry on track generation jobs (duration, tokens used, validation/critic attempt counts, status etc.) is pushed to Grafana via an OpenTelemetry (OTel) Collector running on the Pi

### Generation workflow

![Generation workflow: a queued job retrieves context (style by vector search, chords and scales by rule-based lookup), then loops through Generate, Validate, Repair and Critic; any LLM error or exhausted attempt falls back to a rule-based generator, which is re-validated once before the track is persisted](docs/images/generation-workflow.svg)

#### Normal path

Once a job has been accepted and reaches the front of the queue, the first stage in the workflow is to retrieve additional context for the generation agent to use. Style info is looked up from the vector knowledge base using Voyage AI's embedding model. Other context like chord and scale details are retrieved using deterministic rules. The Generate agent takes the user's request and retrieved context and builds a prompt to send to an LLM, the result of which is the JSON representation of a candidate track.

The candidate is run through a deterministic validation stage, which catches issues such as a track that doesn't have the required number of bars; notes outside the expected instrument's range; notes that don't fit the chosen chords/scales; empty bars etc. A music critic agent takes the candidate track, the user's original request and the retrieved context, and evaluates whether it passes a set of defined quality criteria. All criteria must be passed before the final track is persisted to the Pi's local storage with a unique ID, to be downloaded later by the iOS app.

#### Resilience strategies

Should the deterministic validator find any issues, they are passed to a repair step. The repair step calls an LLM to create a patch, which is then applied to the candidate track to fix the problems identified. This loop continues until the validator finds no more issues, up to a [configurable](api/README.md#configuration) maximum number of attempts. The critic step, should any of its criteria not pass, sends any citations back to the generate step so that a new track can be generated armed with what went wrong in the previous run. This critic/generate loop can also repeat a configurable number of times.

In the event the maximum number of validation or critic attempts is exceeded, or if an LLM call should fail, a fallback process is triggered. The fallback stage uses a rule-based system to generate a track deterministically. This is intended to ensure the user always gets something usable out of a run. The fallback-generated track still goes through the deterministic validation as a sense-check, but it is not evaluated by the critic.

## Headline decisions

<!-- TODO (you): one or two sentences each on why it matters. Pick ~6 to lead with; candidates below.
     Full list: docs/adr/README.md -->

| Theme | Decision |
| --- | --- |
| Reliability | [0017](docs/adr/0017-patch-based-track-repair.md) Patch-based track repair over full regeneration; [0028](docs/adr/0028-silent-bar-repair-via-whole-part-replacement.md) silent-bar repair |
| Quality | [0029](docs/adr/0029-critic-tool-call-over-per-criterion-structured-output.md) Tool-forced single verdict for the Theory Critic |
| Safety net | [0034](docs/adr/0034-rule-based-fallback-generator-bypasses-critic.md) Rule-based fallback bypasses the critic loop |
| Cost | [0036](docs/adr/0036-per-step-model-tier-split-for-cost.md) Per-step model and Flex-tier split |
| Measurement | [0027](docs/adr/0027-generation-eval-harness.md) End-to-end eval harness; results in [`docs/eval-results.md`](docs/eval-results.md) |
| Invocation model | [0013](docs/adr/0013-async-job-based-generation-invocation.md) Async job-based generation; [0037](docs/adr/0037-streaming-run-for-cancellable-generation-jobs.md) cancellable streaming runs |
| Platform / deploy | [0008](docs/adr/0008-agent-framework-aot-compatibility.md) Native AOT + Agent Framework; [0040](docs/adr/0040-testflight-on-every-main-push-with-xcodebuild-cloud-signing.md) TestFlight on every main push; [0047](docs/adr/0047-automatic-rollback-and-release-pruning-on-the-pi.md) automatic rollback on the Pi |
| Style lookup | [0015](docs/adr/0015-in-memory-vector-retrieval-for-rag-kb.md) Small in-memory vector store for matching the style, rather than a vector DB (chords and scales use rule-based lookups) |

## Repository layout

| Path | What |
| --- | --- |
| [`api/`](api/README.md) | ASP.NET Core backend (Native AOT), deployed to a Raspberry Pi. |
| [`app/`](app/README.md) | Native iOS app (SwiftUI). <!-- TODO: stale ("no features yet"); one line on what it does now --> |
| [`docs/`](docs/) | Project scope, [Architecture Decision Records](docs/adr/README.md) and [eval results](docs/eval-results.md). |
| [`api/eval/`](api/eval/README.md) | Generation and critic eval harnesses. |
| `api/local-observability/` | Docker Compose stack for running the OpenTelemetry backend locally. |
| `scripts/` | Pi bootstrap and the local Native AOT / trimming check. |

## Status

<!-- TODO (you): what works today, what doesn't, what's next.
     Facts to draw on (verify each before stating):
     - API released as api-v0.1.0 .. api-v0.1.6 (MinVer from git tags, ADR 0046)
     - iOS builds ship to TestFlight on every main push (ADR 0040)
     - Eval, 2026-10-09/10: 13-case golden set, 3 runs per config, four model configs; chosen config
       gpt-6.1-sol / gpt-5.6-luna / gpt-5.6-luna: 39/39 pass, 0 fallbacks, ~$0.031 per track
       (docs/eval-results.md is the source; re-check numbers if it changes)
     - Vibe mode: not built
     - Stretch goals from the scope doc not done: fretboard visualisation, server-side audio export, saved presets
     - APNs push implemented (ADR 0039) -->

## Building

Requires the .NET 10 SDK (pinned in [`global.json`](global.json)).

```bash
dotnet build api/BackingTrack.sln
dotnet test  api/BackingTrack.sln
```

See [`api/README.md`](api/README.md) for local configuration and deployment.

Requires Xcode 26+ and [XcodeGen](https://github.com/yonaskolb/XcodeGen) for the iOS app — see
[`app/README.md`](app/README.md).

## License

[MIT](LICENSE).
