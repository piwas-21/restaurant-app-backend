# ADR-001 — Custom CQRS mediator (instead of MediatR)

**Status:** Accepted
**Date:** 2026-04-26
**Author:** mahmutkaya
**Reviewers:** —
**Implements / supersedes:** Sprint 1 task 1.5 / 1.6 (`MediatR` package and unused imports removed in d28730f and the chore/sprint-1-completion MR)
**References:**
- [RestaurantSystem.Api/Abstraction/Messaging/](../../RestaurantSystem.Api/Abstraction/Messaging/) — interfaces (`ICommand`, `ICommandHandler<,>`, `IQuery<>`, `IQueryHandler<,>`)
- [RestaurantSystem.Api/Common/CustomMediator.cs](../../RestaurantSystem.Api/Common/CustomMediator.cs) — implementation
- [Program.cs DI registration](../../RestaurantSystem.Api/Program.cs)

---

## Context

The codebase started using [MediatR](https://github.com/jbogard/MediatR), the de facto CQRS mediator for .NET. Two forces drove the decision to replace it:

1. **MediatR licensing transition.** Jimmy Bogard's MediatR moved to a paid commercial-license model in 2024; the OSS-licensed versions are frozen at older API surfaces. Continuing on the OSS line means accumulating a known-stale dependency on a critical-path abstraction; moving to paid MediatR adds a recurring cost for a feature surface we mostly don't use.
2. **Surface area we actually need.** RUMI uses MediatR for exactly two things: `Send(command)` and `Send(query)`. We don't use notifications/streams/pipeline behaviours. The full MediatR API is overkill for our surface.

The existing `CustomMediator` ([RestaurantSystem.Api/Common/CustomMediator.cs](../../RestaurantSystem.Api/Common/CustomMediator.cs)) covers our usage with one resolution call into `IServiceProvider` per dispatch. The `MediatR` NuGet reference and `using MediatR;` imports were unused at the time of removal — they were dead dependencies.

## Decision

**We use a custom in-process CQRS mediator (`CustomMediator`) and do not depend on `MediatR`.**

The mediator surface is exactly:

```csharp
// In RestaurantSystem.Api/Abstraction/Messaging/
public interface ICommand<TResult> { }                     // command with result
public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}
public interface IQuery<TResult> { }
public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}
```

> A void `ICommand` interface also exists in `Abstraction/Messaging/` but is currently
> unused — every command in the codebase returns `ApiResponse<T>` or similar. If a
> truly fire-and-forget command lands, document the dispatch pattern here.

Controllers dispatch via:
```csharp
var result = await _mediator.SendCommand(command);
var result = await _mediator.SendQuery(query);
```

Handlers are registered via assembly scanning in `Program.cs` extension methods.

## Consequences

### Positive
- **Zero third-party dependency** for a critical-path abstraction.
- **Surface area is exactly what we use** — easier to reason about, no extension points we'd have to police.
- **Free** — no recurring license cost or version-drift pressure from a vendor.
- **Replaceable** — if we ever need pipeline behaviours, we can extend our own mediator without breaking 200 handlers.

### Negative
- **No pipeline behaviours out of the box.** MediatR provides cross-cutting concerns (validation, logging, transaction wrapping) via pipeline behaviours. We currently do these ad-hoc in handlers (FluentValidation auto-runs via filter; logging is per-handler). If we want middleware-style pipelines later, we'll have to add it.
- **No notifications / streams.** We don't use them today. If we need pub-sub, we'll either build it on `IMediator`-style notifications or use a separate event bus.
- **Documentation gap.** MediatR users joining the project will look for it. This ADR + [CLAUDE.md §3](../../CLAUDE.md#3--architecture) call this out.

### Mitigation for the negatives
- FluentValidation runs as an MVC filter (`AddValidatorsFromAssemblyContaining<Program>()` in `Program.cs`); we get validation cross-cutting without pipeline behaviours.
- Logging is currently inline; if it becomes noise, we add a thin `LoggingHandler<TCommand, TResult>` decorator wrapper. ~30 LOC.
- If notifications become needed, evaluate Wolverine or NServiceBus before re-introducing MediatR.

## Alternatives considered

### Alternative A: Stay on (older OSS) MediatR
Pinning the last OSS MediatR version was the path of least resistance, but the version is feature-frozen and would slowly accumulate compatibility friction as `Microsoft.Extensions.DependencyInjection` evolves. We use ~5% of the API; carrying the rest is unhelpful weight.

### Alternative B: Move to paid MediatR
Recurring per-developer licensing for a feature surface we don't use. Not justified by the value delivered.

### Alternative C: Use Wolverine (or another modern CQRS framework)
Wolverine is a strong choice for new greenfield code, but introduces concepts (sagas, durable inbox/outbox, message routing) far beyond what we need today. Switching costs would dwarf the benefit at the current scale.
