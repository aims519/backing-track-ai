# 0015. Self-written in-memory vector retrieval over a vector DB for the RAG knowledge base

Status: Accepted

## Context

Precise Mode's agentic workflow needs a retrieve step: before generating, look up relevant
music theory context (scales, chord qualities, style conventions) to ground the LLM call rather
than relying on its unaided recall. That means picking both a knowledge-base content format and
a retrieval mechanism.

ADR 0008 already established that Native AOT is a hard constraint on this project, and made
AOT-compatibility a mandatory audit for any new RAG/vector-store dependency before it's adopted,
not an afterthought. That constraint shaped this decision directly.

The knowledge base itself turned out to be small and enumerable by design: chords and scales
are keyed by *quality* (e.g. `maj7`, `dorian`), not by root note — concrete pitches are derived
at request time by transposing a stored interval pattern — so the full KB across
chords/scales/styles is on the order of a few dozen hand-authored entries, not an imported
corpus. Content lives as JSONL files under
`api/src/BackingTrack.Generation/KnowledgeBase/Content/`.

## Decision

- Retrieval is a self-written, brute-force in-memory cosine similarity search — no vector
  database. Vectors are normalized once at load time so scoring reduces to a dot product; a
  full scan over a few dozen to a few hundred entries is comfortably sub-millisecond, so an ANN
  index (HNSW, IVF, etc.) buys nothing at this scale.
- Embeddings are generated via Voyage AI's hosted API. The KB corpus is embedded once at
  dev/authoring time and the resulting vectors are shipped precomputed alongside the JSONL
  content; only the user's query is embedded at runtime, one call per generation request.

## Alternatives considered

- **A third-party AOT-compatible vector database** (e.g. an embedded vector store library) —
  rejected. At this data scale it would add a new dependency requiring the same AOT-safety
  audit ADR 0008 calls for (checking for `IsAotCompatible`, trim annotations, searching its
  issue tracker), for a performance capability (approximate nearest-neighbor search at scale)
  this KB will never need.
- **Open-source embedding models run locally on the Pi** (e.g. via Hugging Face) — rejected.
  Running any model locally on the Pi at request time directly reverses the scope doc's
  deliberate, explicit decision that the Pi orchestrates API calls and never runs AI models
  itself. Generating the KB corpus embeddings locally at dev time only (not at runtime) would
  have been fine, but the query embedding still needs to happen at runtime, and it must use the
  same model as the corpus to produce comparable vectors — so runtime consistency would have
  forced local inference on the Pi either way.
- **Hugging Face's hosted Inference API** (open-source model, called over HTTP like Voyage) —
  viable and would have kept the Pi's orchestrator-only architecture intact, but rejected in
  favor of Voyage. Voyage's free tier is a 200-million-token one-time allocation per account;
  HF's is a $0.10/month credit that resets monthly and is explicitly documented as "subject to
  change" — a weaker guarantee for infrastructure meant to run indefinitely. Both are
  effectively free at this project's volume, but Voyage's terms are the more comfortable margin
  to build on.

## Consequences

- The "comfortably sub-millisecond" claim above is now measured, not assumed. A BenchmarkDotNet
  suite (`api/benchmarks/BackingTrack.Generation.Benchmarks`, `InMemoryVectorStoreBenchmarks`)
  covers 50/200/500-entry KBs with 1024-dimension vectors (matching Voyage's real output size).
  On the actual Raspberry Pi deployment target — run in-process, since the Pi has no .NET SDK
  installed under the AOT-only deployment model — a single `Search` call takes ~29 µs at 50
  entries, ~102 µs at 200, and ~257 µs at 500: 3-4x under the 1ms threshold even at the top of
  the tested range, and scaling roughly linearly with entry count as expected for a brute-force
  scan. (For reference, the same suite on a dev machine measured ~13-113 µs over the same range;
  the gap is expected weaker-hardware slowdown, not a different growth pattern.)
- No new AOT-audit burden: the retrieval mechanism is plain C# (array iteration, dot products),
  not a third-party library.
- Voyage AI becomes an external runtime dependency for query embedding — one more API key to
  manage via the non-committed environment file (alongside the LLM key), and one more network
  call in the generation request path alongside the LLM call itself, with the latency that
  implies.
- This approach does not scale past roughly the low thousands of KB entries without revisiting
  the decision — acceptable for a hand-authored, deliberately small knowledge base, but a real
  ceiling if the KB ever grows substantially (e.g. importing a larger theory corpus later would
  need a different retrieval mechanism, not just more entries).
- Corpus embeddings are a manual dev-time step: any time KB content changes, the precomputed
  vectors must be regenerated and re-shipped — there's no automatic sync between the JSONL
  content and its embeddings.
