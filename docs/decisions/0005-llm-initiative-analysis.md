# ADR 0005: Evidence-validated LLM initiative analysis

- Status: Accepted
- Date: 2026-09-11

## Context

Deterministic snapshots and Project Memory describe the current repository but do not reason about arbitrary new initiatives. Sending the repository or complete graph to a model would be expensive, difficult to audit, and unsafe. A model can also invent components or relationships that do not exist.

## Decision

Use a two-stage, model-agnostic reasoning pipeline behind `IReasoningProvider`.

Stage 1 receives only the initiative and returns a schema-validated `InitiativeUnderstanding`. Deterministic lexical retrieval then searches exhaustive snapshot entities, project aliases, members, namespaces, and bounded graph neighbors. Markdown memory represents selected context but is never the exhaustive lookup source.

Stage 2 receives the structured understanding, compact repository and Git facts, selected managed notes, evidence identities, relative locations, and selected graph relations. It returns a schema-validated `InitiativeAnalysis` with `REUSE`, `EXTEND`, `CREATE`, and `AVOID_MODIFYING` recommendations or `NEEDS_CLARIFICATION`.

## Evidence and safety

The model reasons; tools prove. A deterministic validator checks repository, branch, project, entity, location, relation, and resolution evidence against the current snapshot. `REUSE`, `EXTEND`, and `AVOID_MODIFYING` cannot be presented as validated without their required existing evidence. `CREATE` remains a proposal. Facts, inferences, proposals, and unknowns are distinct.

Remote reasoning requires the explicit `--allow-remote` flag. The OpenAI implementation reads `OPENAI_API_KEY` from the process environment, uses the Responses API with strict Structured Outputs, and disables response storage. `scan` and `memory sync` do not instantiate the provider or use the network.

Initiative text is the only repository-independent data sent in stage 1. Stage 2 never receives source bodies, repository files, complete snapshots, environment files, credentials, or configuration contents. Initiative and memory content are isolated as untrusted user data rather than system instructions.

## Token strategy

Context selection uses deterministic integer ranking, centralized candidate limits, depth-one graph expansion, and conservative character-based token estimates. Whole lower-ranked notes are pruned before a hard input limit is exceeded; strings and evidence records are not cut mid-value. Estimated usage is recorded separately from provider-reported input, cached, and output tokens.

## Consequences

The product can analyze unrelated new initiatives without requiring prior similar work, while every repository claim remains auditable. The first provider is isolated in Infrastructure and can be replaced without changing Core. Retrieval is intentionally lexical and does not include embeddings, source-body retrieval, a full call graph, dependency-injection resolution, or automatic code generation.
