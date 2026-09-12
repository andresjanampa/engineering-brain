# ADR 0006: Evaluation and remote context preview

## Status

Accepted

## Problem

Engineering Brain can retrieve repository context and ask a provider for architecture recommendations, but further retrieval or LLM changes cannot be justified safely without repeatable quality measurements. Remote payload composition also needs to be inspectable before any network request.

## Decision

Use a versioned golden evaluation corpus and an offline harness. Each case supplies an expected structured initiative interpretation so retrieval quality is measured independently of a live model. The harness reports entity and project ranking metrics, test noise, context size, budget use, evidence validation, clarification behavior, and category results. A versioned baseline changes only through an explicit CLI option, and ordinary runs detect meaningful regressions.

One repository scan and one Project Memory sync are reused across every case in a suite. Runtime result JSON is stored outside the repository; golden fixtures and the accepted baseline are versioned.

## Preview

`brain analyze --preview` uses a clearly labeled deterministic interpretation and performs no remote call. It reports the planned call metadata, selected identities and note paths, context counts, token estimates, and security results without printing the initiative or full context. Its manifest stores metadata only.

## Safety guards

Outbound context is represented by typed segments. Before a provider call, guards reject source-body segments, likely secrets, unnecessary absolute local paths, and raw snapshot payloads. Preview and real analysis use the same validation boundary.

## Reasoning effort

OpenAI reasoning effort is provider-specific, independently configurable for interpretation and architecture analysis, constrained to SDK-supported values, and recorded in usage metadata.

## Consequences

The initial corpus is intentionally small and repository-specific, so its metrics are directional rather than statistically general. Future decisions about embeddings, source retrieval, analyzers, or ranking changes must begin with measured failures and compare against the accepted baseline. The harness does not evaluate subjective model intelligence and performs no real provider call.
