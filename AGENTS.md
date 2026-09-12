# Engineering Brain Agent Guide

Keep these rules true as the product evolves:

- Stay local-first and security-first. Do not upload source, dump repositories into prompts, log secrets, or add network dependencies without explicit approval.
- Be evidence-first: important conclusions must trace to files, symbols, lines, relations, and commits when applicable.
- Remain model-agnostic. LLMs reason; deterministic tools prove. Prefer deterministic analysis before probabilistic analysis.
- Keep analysis branch-aware, incremental-ready, impact-aware, and token-aware.
- Inspect consumers and dependencies before recommending a component change.
- Treat previous initiatives as evidence and memory, never hardcoded rules.
- Keep the code graph (objective extracted facts) separate from project memory (derived knowledge and decisions).
- Do not fabricate relationships. Missing data is better than false certainty.
- Prefer semantic evidence over string matching. Never claim semantic resolution when project loading is incomplete or ambiguous.
- Degrade gracefully per project. Clearly labeled partial and syntax-fallback results remain valid; one broken project must not invalidate healthy projects.
- Prefer focused tests for analyzers, especially parsing, semantic resolution, evidence locations, and stable identities.
- Preserve snapshot and CLI backward compatibility as the product evolves, or document and version intentional breaks.
- Avoid infrastructure and abstractions until a demonstrated use case needs them.
