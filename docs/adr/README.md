# Architecture Decision Records

Index of ADRs for the RUMI Backend. New ADRs are numbered sequentially with no gaps.

> **When to write an ADR**: any decision that constrains future implementation choices, has non-obvious tradeoffs, or that you'd want a future agent to understand without re-deriving. Bug fixes don't need ADRs; choosing a library or pattern usually does.
>
> **Format**: copy [ADR-template.md](ADR-template.md) and fill in. Status starts as `Proposed`; flip to `Accepted` on merge.

## Index

| # | Title | Status | Date | Tags |
|---|---|---|---|---|
| [001](ADR-001-custom-cqrs-mediator.md) | Custom CQRS mediator (vs MediatR) | Accepted | 2026-04-26 | architecture, mediator |
| [002](ADR-002-soft-delete-strategy.md) | Soft-delete via global query filter | Accepted | 2026-04-26 | data, ef-core |
| [003](ADR-003-jwt-scope-and-claims.md) | JWT scope and claim shape | Accepted | 2026-04-26 | auth, security |

## Conventions

- Filename: `ADR-NNN-kebab-case-title.md`
- Numbering: gap-free; if rejected, mark Status: `Rejected` and keep the file (history matters)
- Status progression: `Proposed` → `Accepted` / `Rejected` → `Deprecated` / `Superseded by ADR-XXX`
- One ADR = one decision; if a record grows three sub-decisions, split it
- Reference ADRs from code comments via `// See docs/adr/ADR-NNN.md` when behaviour is non-obvious
