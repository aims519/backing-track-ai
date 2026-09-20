# 0035. Hand-authored JSON schema for the repair step, over auto-generation

Status: Accepted

## Context

Newer OpenAI model families (e.g. `gpt-5.6-terra`) require the Responses API rather than Chat
Completions. Adding Responses-API support (`OpenAiApiKind` on `OpenAiSettings`, selectable per
model slot) surfaced a live failure in the repair step specifically: `RepairStep` asks the model
for a structured `TrackRepairPatch`, and the JSON Schema `System.Text.Json` auto-generates for
that type — via `AIAgent.RunAsync<T>` — is rejected by the Responses API's stricter schema
validator with two distinct `invalid_json_schema` errors, both reproduced live against the eval
harness (`api/eval/BackingTrack.Generation.Eval`):

1. `schema must have a 'type' key` — `NoteEvent`'s polymorphic `"$type"` discriminator (used by
   `NoteCorrection.Original`, via `[JsonPolymorphic]`/`[JsonDerivedType]` for `DrumNoteEvent`/
   `BassNoteEvent`/`ChordVoicingEvent`) is exported as a bare `"const"` with no `"type"` keyword.
   Chat Completions accepted this; Responses does not.
2. `reference can only point to definitions defined at the top level of the schema` — the exporter
   can point a `"$ref"` at a nested subschema (inside a sibling property's own schema) instead of a
   top-level `"$defs"` entry. Chat Completions tolerated this; Responses does not.

Both are documented, real constraints of the Responses/Chat Completions strict-schema subset (see
the [Azure OpenAI structured outputs guide](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs)):
`$defs`/`$ref` are only supported at the schema root, and every constrained value (`enum`,
implicitly `const`) needs an explicit `type`. `System.Text.Json`'s exporter just doesn't shape its
output to match.

## Decision

Hand-author the JSON schema for `TrackRepairPatch` as a static file
(`api/src/BackingTrack.Generation/Steps/TrackRepair/track-repair-patch.schema.json`), written
directly against the documented strict-schema subset, and pass it explicitly via
`ChatOptions.ResponseFormat`.

`AIAgent.RunAsync<T>` (`Microsoft.Agents.AI`) always builds and overwrites `ResponseFormat` with
its own generated schema — confirmed by decompiling `Microsoft.Agents.AI.Abstractions` — and
exposes no schema-transform hook, so using our own schema meant dropping to the non-generic
`RunAsync`/`IChatClient.GetResponseAsync` and deserializing the response text ourselves.

Once off `RunAsync<T>`, the only thing `AIAgent` was still doing for this step was auto-injecting
a system prompt — no tools, no multi-turn session state. So `RepairStep` now takes
`[FromKeyedServices("repair")] IChatClient` directly and builds the system+user message list
itself, matching the existing `CriticAiClient` pattern in the same codebase. `AddTrackRepair` no
longer registers an `AIAgent` at all.

## Alternatives considered

- **Runtime schema-patching decorator** (`StrictJsonSchemaChatClient`, an `IChatClient` wrapping
  `DelegatingChatClient`): walked the auto-generated schema on every call, inlining every `$ref`
  and back-filling missing `type` keys. Verified working live. Rejected because mutating a
  generated schema via generic JSON-tree surgery on every request is more indirection than a
  schema that only needs to be correct once.
- **Simplify `NoteEvent`**: replace the polymorphic `NoteCorrection.Original` with a flat
  `OriginalNoteEvent` record carrying a `NoteEventKind` enum instead of a `"$type"` discriminator.
  Would fix the `const`-without-`type` bug at the source, but not the separate `$ref` bug (a
  general `System.Text.Json` schema-dedup behaviour for any repeated substructure, not specific to
  polymorphism) — the patch or a hand-authored schema would still be needed regardless. Deferred:
  a domain-type change is a bigger, separate decision from schema plumbing.

## Consequences

- The schema file is a second source of truth: it must be kept in sync by hand whenever
  `TrackRepairPatch`'s C# shape changes. Nothing enforces this at compile time — a mismatch would
  surface as a deserialization failure at runtime, not a build error.
- In exchange, repair's structured output no longer depends on how a given `System.Text.Json`/SDK
  version happens to shape an auto-generated schema, and is verified directly against the
  documented strict-schema subset rather than an implementation detail of the exporter.
- `RepairStep` no longer depends on `Microsoft.Agents.AI` at all, simplifying it to the same
  `IChatClient`-based shape as `CriticAiClient`.
