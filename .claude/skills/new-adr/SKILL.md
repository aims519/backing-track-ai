---
name: new-adr
description: Create a new Architecture Decision Record in docs/adr/ for this project. Use when a non-obvious structural or technical decision has just been made or discussed — a real tradeoff with a rejected alternative, not routine setup — and it should be captured for the portfolio writeup. Triggers on "add an ADR", "document this decision", "/new-adr".
---

Create a new ADR file in `docs/adr/` for the decision under discussion.

## Steps

1. List `docs/adr/*.md` (excluding `template.md`) and find the highest existing number to pick the next one, zero-padded to 4 digits (e.g. `0001`, `0002`).
2. Derive a short kebab-case slug from the decision (e.g. `minimal-apis-over-controllers`).
3. Copy the structure from `docs/adr/template.md` — Status, Context, Decision, Alternatives considered, Consequences — into `docs/adr/NNNN-slug.md`.
4. Fill in each section from the actual conversation/decision, not generic placeholders:
   - **Context**: what constraint or goal prompted the decision (pull from the scope doc at `docs/backing-track-generator-scope.md` if relevant).
   - **Decision**: what was actually chosen.
   - **Alternatives considered**: what was rejected and the concrete reason (an error, a warning, a measured tradeoff — not a vague "it's less standard").
   - **Consequences**: what this makes harder or forecloses, stated honestly, not just the upside.
   - Status is `Accepted` unless the user says the decision is still tentative.
5. If `api/README.md` or another README already links `docs/adr/`, no further action needed. Otherwise, mention that the ADR was created and where.

## What NOT to do

- Don't create an ADR for routine scaffolding (adding a folder, a standard package, a template default) — only for decisions with a genuine rejected alternative.
- Don't pad sections with boilerplate to look thorough. A short, honest ADR beats a long generic one.
