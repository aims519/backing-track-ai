# 0048. Additive-only API changes instead of URL versioning

Status: Accepted

## Context

"How is the API versioned?" The API has one consumer, the iOS app, and the same person owns both
sides. There are no third-party clients and no public consumers.

The compatibility risk is timing, not audience. The API deploys to the Pi on every push to `main`
that touches `api/**` (`deploy.yml`), so a server change is live within minutes. The app ships to
TestFlight on every push to `main` ([ADR 0040](0040-testflight-on-every-main-push-with-xcodebuild-cloud-signing.md)),
but a tester's phone keeps whatever build it has until they update. At any moment an old app can
be talking to a newer API.

The one breaking change so far shows the shape of the problem. On 2026-09-29, #104 made the client
accept both 200 and 202 from `POST /tracks/generate`, and #105 changed the API to return 202. Both
landed the same day, so any TestFlight build from before #104 would have failed on every submit.
Nothing in the repo records whether such a build was in anyone's hands.

[ADR 0046](0046-minver-from-git-tags-for-api-version.md) gives the API a version number, but that
number is for telemetry (`service.version`). It says which deploy produced a trace and is not a
contract version.

## Decision

No `/v1` routes and no version header. Compatibility is kept by rule instead:

1. **Server changes are additive.** Add fields and endpoints. Never rename or remove a field, and
   never change the meaning of a field, status code or job state.
2. **New request fields are optional with a safe default.** `GenerationRequestDto.Validate` would
   otherwise return a 400 to an old app that doesn't send the new field.
3. **The client tolerates change.**
   - Unknown JSON fields are ignored. `Codable` and `System.Text.Json` both do this by default.
   - Unknown enum values are decoded rather than thrown on: `JobStatus.unknown(String)` for job
     states, and `decodeIfPresent ... ?? []` for optional track arrays in `CandidateTrack`.
   - When a status code has to change, the client accepts both codes before the server switches.
4. **A break that can't be avoided goes through expand/contract.** Ship a tolerant client, wait for
   testers to update, change the server, then tighten the client. The 200 to 202 change followed
   this order, minus the waiting. #104 was the expand step, #105 the server change, and #146
   tightened the client back to exactly 202 (`acceptedStatuses: [202]`, covered by
   `submitRequiresExactly202`). The client no longer accepts 200 from that endpoint.

Which builds are in use can be read from existing telemetry, so no client-version header is
added. The ASP.NET Core OpenTelemetry instrumentation records `user_agent.original` on server
spans, and `URLSession`'s default `User-Agent` carries the build number. A real span in Grafana
shows `BackingTrack/117 CFNetwork/3896.100.1.2.1 Darwin/27.0.0`. The build number is CI's
`run_number + 100` (`ios-release.yml`) and identifies one TestFlight build uniquely. The
marketing version isn't in it, which doesn't matter, since the build number already identifies
the build.

## Alternatives considered

**URL versioning (`/v1/tracks/...`).** Rejected. A second version means parallel routes, a second
set of DTOs registered in the source-generated JSON context
([ADR 0007](0007-source-generated-json-for-aot-endpoints.md)), and a second set of tests, all kept
alive for a consumer that updates when the owner ships a build. It protects third parties, and
there aren't any. It also wouldn't remove the timing problem. An old app would be pinned to `v1`,
so `v1` would have to be kept running for as long as any tester might still hold an old build.

**An explicit `X-App-Version` request header.** Deferred. It would add the marketing version, but
the `User-Agent` already gives the build number, which is the finer identifier. The header becomes
worth adding when a breaking change is planned and the question is who is still on an old build,
or when a minimum-version gate needs the value server-side.

## Consequences

- **Nothing enforces the rule.** It rests on discipline and review. A rename or removal that
  slips through compiles, passes the existing tests and breaks old builds in the field. There is
  no contract test pinning the shape of `GenerationJobDto` or the status codes the client relies
  on.
- **There is no way to tell an old build to update.** The server can't refuse, warn or redirect
  an outdated app. If a break is ever unavoidable, the only mitigation is waiting for testers to
  update, and the only way to tell whether they have is reading build numbers out of traces.
- **The API's shape only grows.** Fields and states that turn out to be wrong stay in the
  contract. Deprecation means "stop reading it", with removal deferred until no old build is in
  circulation.
- **Client tolerance is a standing cost.** New enum-like values on the wire need an `.unknown`
  path in the app, and "accept both" code lives in the client between the expand and contract steps.
- **Build visibility depends on the `User-Agent`.** If the app ever overrides it, or a proxy
  strips it, the build number stops reaching the traces.

Optional follow-ups, in priority order:

1. A contract test that pins the response shape and the client-relied-on status codes, so a
   rename or removal fails CI.
2. `X-App-Version`, if the `User-Agent` proves insufficient.
3. A minimum-version gate (a 426 response plus an update-required screen in the app). That is a
   decision with its own tradeoffs and would need its own ADR.

**Revisit when** a second client appears or the API gains public consumers. At that point the
audience the versioning would protect exists, and URL versioning should be reconsidered.
