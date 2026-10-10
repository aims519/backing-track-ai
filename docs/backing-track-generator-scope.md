# Guitar Backing Track Generator — Project Scope

> **Original scope, kept for context.** This was written before the build started and has not
> been rewritten since, so parts of it no longer match what was built. See the
> [README](../README.md) for the current system and [`docs/adr/`](adr/README.md) for the
> decisions that changed the plan.

## Purpose

A portfolio project demonstrating full-stack engineering and applied generative AI: a native iOS app that generates guitar backing tracks (drums, bass, chords) from user-selected parameters, backed by a self-hosted API on a Raspberry Pi.

The point of the project is the AI engineering — reliable structured generation, validation, and a dual-mode approach to trading precision against realism — not just wiring up an API.

## Platform

**Native iOS app, built in SwiftUI.**

Chosen over a PWA and React Native. A PWA would have been the lower-risk, zero-new-tooling option (Tone.js/Web Audio is mature in a browser), and React Native's Web Audio support depends on an immature bridge library. Native iOS was chosen deliberately: it has the most mature audio tooling (AVFoundation/AudioKit, no bridge-library risk) and the author already has some SwiftUI experience, making this project a chance to build on that.

No App Store distribution required for MVP — runs via Xcode directly onto a personal device.

## Backend

**Raspberry Pi**, always-on, hosting the API. Built in **ASP.NET Core** (the author's primary language at work), published as **Native AOT** for a lean, fast-starting binary suited to the Pi's limited resources, and run as a systemd service.

Considered running the backend on a MacBook instead — rejected. A Mac has far more compute, but "always-on" is impractical for a machine also used daily, and a dedicated Pi better fits a real edge-deployment story for the portfolio.

The Pi's role is lightweight: orchestrate API calls (to an LLM and/or a third-party audio-generation API) and serve results. It does not run any AI model locally — not enough compute for that, and not necessary given the architecture below.

## Core Flow

1. User selects chords, key, tempo, and style in the app.
2. App sends these parameters to the Pi-hosted API.
3. API generates a backing track using one of two modes (below).
4. App plays the result.

## Generation Modes

### Precise Mode (chord-locked)

Built as an **agentic workflow using Microsoft Agent Framework** (the production-ready successor to Semantic Kernel/AutoGen, .NET-native) rather than a single LLM call — this is the piece that demonstrates agentic architecture, a specific goal of the project alongside the AI engineering depth.

- **Retrieve (RAG)**: before generating, the agent retrieves relevant music theory context — scales, chord-progression patterns, style conventions — from an embedded knowledge base, grounding the generation in reference material rather than relying on the LLM's unaided recall.
- **Generate**: using the user's parameters plus the retrieved context, the agent calls a general-purpose LLM (e.g. Claude/GPT) — no separate "music-specialized" model exists as an off-the-shelf API, so the prompt design and retrieval grounding *are* the specialization. Returns structured note/pattern data (drum pattern, bassline, chord voicings) as JSON.
- **Validate**: output is checked against music theory constraints (correct key, chord tones, timing).
- **Iterate/repair**: on validation failure, the agent attempts to auto-correct or regenerate, looping within the agentic workflow rather than failing outright. This validate→repair loop is the core engineering depth of the project.
- **Rule-based fallback generator**: if the agent repeatedly fails validation, a deterministic algorithmic generator (e.g. via `music21`-style pattern logic) produces the track instead, guaranteeing the app never fails to return something playable.
- Structured data is converted to MIDI-equivalent note events and sent to the app.
- Played back in SwiftUI via AVFoundation/AudioKit using **sampled instruments** (not basic synth tones) for realism — mirrors the approach used by comparable apps in this space (e.g. backingtrack.app).
- Fully tempo-adjustable and loopable live on-device, since no re-generation is needed to change playback.

### Vibe Mode (realism over accuracy)

- Same user parameters are turned into a text prompt for a third-party generative audio API (e.g. ElevenLabs Music, Stable Audio).
- Returns a real rendered audio file, played directly (AVAudioPlayer) — richer, more "real instrument" sound.
- Trade-off: no guaranteed chord-by-chord accuracy, and it's a static file (no live tempo/pattern tweaking without re-generating).
- Cost-bearing (unlike Precise mode, these APIs charge per generation, e.g. ElevenLabs ~$0.80/min) — needs a generation limit or "costs a credit" UX framing.
- UI should clearly label this mode as approximate, not a precision practice tool.

## MVP Feature List

- Chord/key/tempo/style input UI
- Precise mode: agentic RAG-grounded generation (Microsoft Agent Framework) → validate/repair loop → rule-based fallback → sampled playback
- Vibe mode: third-party audio API → direct playback
- Loop and tempo controls (Precise mode)
- Pi-hosted API, deployed as a persistent service

## Stretch Goals

- Chord/fretboard visualization component, synced to playback — deferred from MVP to keep initial scope focused on generation + playback; revisit once the core dual-mode generation is working
- Server-side audio export/render of Precise-mode tracks (via FluidSynth + soundfont on the Pi) for download/sharing
- Saved presets / recent tracks (SQLite on the Pi — no login needed for single-user use)
- Additional styles/genres
- App Store distribution

## Deployment & Infrastructure

**Build & deploy pipeline.** GitHub Actions (Linux runner) cross-compiles the API as Native AOT targeting `linux-arm64` (requires the arm64 cross toolchain: `clang`, `gcc-aarch64-linux-gnu`, `binutils-aarch64-linux-gnu`). The workflow then uses the official `tailscale/github-action` to join the ephemeral runner to the tailnet for the duration of the job (auth-key/OAuth scoped by tag, auto-logged-out on completion), and SSHs into the Pi *over Tailscale* to copy the binary and restart the service. Push to `main` → built → deployed to physical hardware, with no manual steps and no public SSH exposure at any point.

**Process management.** Runs as a systemd service (`Restart=on-failure`, enabled on boot) — sufficient for a single service on a single box. Kubernetes (`k3s`) was considered and deliberately rejected: its value comes from orchestrating multiple nodes/replicas/services, none of which apply here, and its control-plane overhead would work against the lean footprint Native AOT was chosen for.

**Network exposure.** The Pi is reachable only via Tailscale (WireGuard-based mesh VPN) — no port forwarding, no dynamic DNS, no public-internet exposure. This covers *both* traffic paths: the iPhone app calling the API, and CI/CD deploying to it (see above). SSH is never exposed publicly. A Pi-hole custom DNS entry was considered for the app→API path but rejected — it only resolves on the home network, so it wouldn't work away from home, whereas Tailscale's MagicDNS gives an equivalent friendly hostname that works everywhere. Pi-hole stays in place for its normal ad-blocking role, just not for reaching the API.

**API protection & cost control.** The app authenticates to the API with a lightweight shared-secret header (not full user auth — see Non-Goals) to prevent arbitrary use of endpoints that call paid third-party APIs. A simple daily generation cap acts as a backstop against runaway cost from bugs or misuse.

**Secrets management.** LLM and audio-gen API keys are kept out of source control in a non-committed environment file (`chmod 600`), loaded via `EnvironmentFile=` in the systemd unit.

**Logging.** Structured logging (e.g. Serilog to the systemd journal or a local file) specifically around the validation/repair layer — logging validation failures, successful repairs, and rule-based fallback triggers. This produces real numbers for the portfolio writeup (e.g. fallback trigger rate) rather than an unverifiable "it works."

**Storage reliability.** Raspberry Pis are prone to SD card corruption on power loss, which matters more once this is always-on infrastructure. Booting from a USB SSD instead of the SD card (supported on Pi 4/5) is a cheap reliability improvement.

Backups are explicitly out of scope for now — no data on the Pi is critical enough yet to warrant it.

## Non-Goals (for now)

- **User accounts / login.** The app is single-user (Andy's phone, Andy's Pi) — no data isolation is needed. Deliberately not building auth now, but the SQLite schema for saved tracks should stay simple enough that adding a `user_id` column and basic auth later, if multi-user support is ever wanted, is a small addition rather than a rework.

## Differentiation

The core concept (AI-generated backing tracks) already exists commercially (backingtrack.app, Rhythm Studio, ChordsofGuitar.com). The differentiators for this project are:

- End-to-end self-hosted deployment on personal hardware (Pi), not a cloud SaaS
- Dual-mode generation exposing the real tradeoff between structured accuracy and generative realism
- Visible engineering around LLM reliability (validation, repair, fallback) rather than a raw API wrapper
- Agentic architecture (Microsoft Agent Framework) and RAG-grounded generation, not just a prompt-and-parse API call