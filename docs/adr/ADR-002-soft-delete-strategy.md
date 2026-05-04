# ADR-002 — Soft-delete via global query filter

**Status:** Accepted
**Date:** 2026-04-26
**Author:** mahmutkaya
**Reviewers:** —
**Implements / supersedes:** —
**References:**
- [RestaurantSystem.Infrastructure/Persistence/ApplicationDbContext.cs](../../RestaurantSystem.Infrastructure/Persistence/ApplicationDbContext.cs)
- [RestaurantSystem.Domain/Common/BaseEntity.cs](../../RestaurantSystem.Domain/Common/BaseEntity.cs) — `IsDeleted` field
- EF Core docs: https://learn.microsoft.com/ef-core/querying/filters

---

## Context

Restaurant data has a recoverability requirement. A waiter mis-cancels an order; an admin accidentally removes a menu item; an inactive customer's account is deleted but its order history must remain queryable for accounting. We need "delete that doesn't delete":

- The row is removed from default queries — invisible to the application.
- The row is preserved on disk — recoverable, auditable, and required for foreign-key integrity (orders reference deleted products).
- The cost of "delete" stays low (no cascade churn).

Hard deletes would force either denormalised snapshots (DTO copies into `OrderItem`) or cascade-delete chains that take orders with them.

## Decision

**All entities that participate in soft-delete inherit from a base type with an `IsDeleted` boolean, and `ApplicationDbContext.OnModelCreating` registers a global query filter that excludes soft-deleted rows from every query.**

```csharp
modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
```

Operations:
- **Delete** = `entity.IsDeleted = true; await _context.SaveChangesAsync()`. Never `_context.Remove(entity)`.
- **Restore** = `entity.IsDeleted = false; await _context.SaveChangesAsync()`. Use `IgnoreQueryFilters()` on the lookup.
- **Read deleted** = `_context.Orders.IgnoreQueryFilters().Where(...)`. Use only in admin/audit contexts.
- **Foreign keys** to soft-deleted rows are preserved; navigation properties from soft-deleted parents return null in default queries (consumers must handle).

## Consequences

### Positive
- Recoverability is uniform across all entities — no per-feature implementation drift.
- Foreign keys remain valid; orders referencing a deleted product still load.
- "Delete" is a fast UPDATE; no cascade-delete risk.
- Audit trail intact: deleted rows still have `CreatedAt`, `UpdatedAt`, `CreatedBy`, etc.

### Negative
- **Storage grows unbounded.** Soft-deleted rows accumulate forever unless a purge job is added. Acceptable for a single-restaurant deployment; will need attention if/when we scale or add SaaS multi-tenancy.
- **`IgnoreQueryFilters()` discipline.** Forgetting it in admin reports = "where did the data go?". Forgetting NOT to use it in normal app code = "why is the cancelled order showing?". Code review must catch this.
- **Index design.** Composite indexes on hot tables should include `WHERE IsDeleted = false` (PostgreSQL partial indexes) to avoid scanning deleted rows. Not all indexes do this today — flagged as cleanup.
- **GDPR right-to-be-forgotten** — soft-delete does NOT satisfy "erase my data". For account deletion specifically, a separate hard-delete pipeline (`AccountCleanupService`) finalises after a retention window. See its docstring.

### Mitigation for the negatives
- Open a follow-up issue to add periodic purge jobs once retention policies are decided per-entity.
- `BackgroundServices/AccountCleanupService` handles hard-delete for accounts; mirror the pattern if other entities need true erasure.
- Code review: any new `_context.Remove(...)` is a smell — flag it. Any `IgnoreQueryFilters()` in non-admin code is a smell.

## Alternatives considered

### Alternative A: Hard delete with denormalised snapshots
Denormalise the relevant fields into the referencing entity (e.g. copy `productName`/`productPrice` into `OrderItem`). Then delete the parent freely. Matches what payment processors do. Rejected because it duplicates data across many features (orders, reservations, fidelity points), and any change to the parent shape breaks every consumer.

### Alternative B: Audit-table pattern
Move deleted rows to a `<Entity>Deleted` table on delete. Cleaner separation but doubles the schema surface and requires custom restore tooling. Overkill for current scale.

### Alternative C: Per-entity soft-delete (no global filter)
Add `WHERE IsDeleted = false` to every query manually. Rejected — guaranteed to drift; new queries will forget; the global filter is the cheapest invariant.
