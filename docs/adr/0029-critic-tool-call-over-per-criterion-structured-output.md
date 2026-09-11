# 0029. Tool-forced single verdict over per-criterion structured output for the Theory Critic

Status: Accepted

## Context

The Theory Critic is the subjective-quality gate that runs after the deterministic
validate/repair loop passes (`Steps/Critic/`, wired up in commit 37df872, not yet connected to
`GenerationWorkflowBuilder`). It judges things the rule-based `TrackValidator` structurally can't
— style fit, voice leading, whether a chord voicing tracks the progression underneath it — across
a fixed set of dimensions (`CriticCriterion`: chord-tone adherence, voice leading, rhythm/feel,
and more to follow).

The first pass built this the same way `GenerateStep`/`RepairStep` talk to their LLMs: one
`AIAgent.RunAsync<CriticCriterionResult>` structured-output call per dimension
(`ReviewRhythmAndFeelAsync`, with a sibling planned per remaining `CriticCriterion`). Each call
re-serializes the full `CandidateTrack`/`GenerationJob` into its prompt, and
`ReviewCandidateTrackAsync` hand-assembles the results via `Task.WhenAll` into a
`Dictionary<CriticCriterion, CriticCriterionResult>`. Each `CriticCriterionResult` carries its own
independent 1–10 `Score` — there is no single point where the review becomes one pass/fail
decision, and nothing enforces that every dimension actually gets reviewed (a criterion added to
the enum but not wired into the per-criterion task list, the prompt's `switch`, and the results
dictionary would simply never run, with no error).

The critic is also meant to feed two things downstream that both expect one coherent result per
candidate, not per-dimension fragments: a pass/fail gate that decides whether to trigger
regeneration, and a structured issue list (with citations back into the retrieved knowledge base)
to fold into the next generation attempt's prompt.

## Decision

The critic makes one LLM call per review instead of one per dimension, and that call must end in
a single forced tool call rather than free-form or per-type structured text.

- All context (`CandidateTrack`, `GenerationJob`, matched style/chords/scales) and every
  dimension's instructions go into one prompt, sent once.
- A `submit_verdict` tool is declared via `AIFunctionFactory.CreateDeclaration` — a
  schema-only `AIFunctionDeclaration` (it extends `AITool`, not `AIFunction`; it has no
  `InvokeAsync`), so there's no delegate for anything to auto-invoke and no risk of an
  agentic tool-invocation loop. Its schema is built with `AIJsonUtilities.CreateJsonSchema`
  against a source-generated `JsonSerializerContext`, following the AOT-safety rule from ADR
  0007/ADR 0016 (no reflection-based `JsonSerializerOptions` resolvers) and the general
  tool-calling AOT guidance from ADR 0008.
- The call goes through the keyed `"critic"` `IChatClient` directly via `GetResponseAsync`
  with `ChatOptions.Tools = [submitVerdictTool]` and
  `ChatOptions.ToolMode = ChatToolMode.RequireSpecific("submit_verdict")` — not
  `AIAgent.RunAsync<T>`, which returns structured *text*, not a tool call.
- The tool's schema requires an assessment for every `CriticCriterion` in one payload
  (pass/fail, a violation description, and a cited knowledge-base chunk id when failed). The
  resulting `FunctionCallContent`'s arguments (`JsonElement`-valued) are reconstructed via
  `JsonObject`/`JsonNode` and deserialized through the `JsonTypeInfo<T>` overload, again for
  AOT safety.
- The LLM reports findings; it does not decide pass/fail. Application code derives the
  aggregate score and the gate from the returned per-dimension results.

## Alternatives considered

- **Keep the per-criterion `RunAsync<T>` structured-output pattern**, adding one more call and
  prompt per remaining `CriticCriterion`. Rejected: it has no natural point to derive a single
  verdict/gate from N independent scores; a forgotten dimension fails silently rather than being
  a detectable schema violation; each call re-sends the full track/job JSON; and `Task.WhenAll`
  discards already-paid-for sibling results when one call fails. It does keep real upsides —
  independently tunable prompts per dimension, genuine parallelism, easier isolated debugging —
  given up by this decision (see Consequences).
- **Tool-calling as the goal, rather than a means to it.** A single always-forced,
  never-actually-invoked tool call is functionally close to structured output under the hood, so
  using a tool here isn't inherently more "agentic" by itself. The justification for this decision
  is the practical set above — one derivable verdict, matching what the repair loop and eval work
  downstream need — not that a tool is involved.

## Consequences

- `ReviewRhythmAndFeelAsync` and its planned per-criterion siblings, the `switch` in
  `GenerateRequestPrompt` picking a criterion's instructions, and the manual
  `Task.WhenAll`/dictionary assembly in `ReviewCandidateTrackAsync` all go away, replaced by one
  call and one parse step. `CriticCriterionResult` and per-criterion prompt constants
  (`CritiqueRhythmAndFeelInstructions`) are superseded by a single schema type and one combined
  system prompt covering every dimension.
- Losing per-dimension isolation is a real cost: one prompt now has to teach the model every
  rubric at once, which is harder to tune than iterating on one focused criterion's instructions
  in isolation, and a change aimed at one dimension's judgment can shift another's. `Task.WhenAll`
  parallelism across dimensions is also gone — the review is now one sequential round trip.
- A missing dimension in the model's response is a schema/parsing problem to handle explicitly
  (throw, or treat as a failed assessment) rather than a case that can't occur because it was
  never wired up in the first place.
- The pass/fail gate and any scoring threshold now live entirely in application code reducing the
  per-dimension results — the model's job is limited to reporting findings, never deciding
  outcome, which keeps the gate's logic centralized and testable independent of prompt changes.
- This is the first place in the codebase that does real LLM tool-calling rather than
  `RunAsync<T>` structured output; the AOT-safe pattern established here (declaration-only tool,
  `JsonObject`-mediated argument parsing) is the template for any future tool the project adds.
