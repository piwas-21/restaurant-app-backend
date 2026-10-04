using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>Scrubs operational instructions after retained loyalty scope locks, before unlinking orders.</summary>
internal static class RetainedOrderInstructionsScrubber
{
    private const string Erased = "[erased]";
    private const string Audit = "RetainedOrderInstructionsScrubber";

    internal static async Task ScrubAsync(ApplicationDbContext context, Guid userId,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new ConflictException("Customer instruction erasure requires the retained-order transaction.");

        // soft-delete-bypass: hidden financial orders retain the same customer instructions.
        var orders = context.Orders.IgnoreQueryFilters().Where(order => order.UserId == userId);
        var orderIds = await orders.Select(order => order.Id).ToArrayAsync(cancellationToken);
        if (orderIds.Length == 0)
            return;
        var sessionIds = await orders.Where(order => order.ServiceSessionId.HasValue)
            .Select(order => order.ServiceSessionId!.Value).Distinct().ToArrayAsync(cancellationToken);
        if (await context.OrderAmendmentResolutionOperations.AsNoTracking()
                .AnyAsync(value => orderIds.Contains(value.SourceOrderId)
                    && value.State != OrderAmendmentResolutionOperationState.Resolved, cancellationToken)
            || await context.AccountPaymentAttempts.AsNoTracking()
                .AnyAsync(value => sessionIds.Contains(value.ServiceSessionId)
                    && !AccountPaymentStateRules.KnownUnprotectedStates.Contains(value.State)
                    && value.State != AccountPaymentState.Captured, cancellationToken))
            throw new ConflictException("Resolve the protected order settlement before erasing this customer.");

        var now = DateTime.UtcNow;
        var amendments = await context.OrderAmendments
            .Where(value => orderIds.Contains(value.SourceOrderId)).ToListAsync(cancellationToken);
        foreach (var amendment in amendments)
        {
            amendment.RequestJson = RetainedOrderPayloadRedactor.Redact(amendment.RequestJson)!;
            amendment.ChangesJson = RetainedOrderPayloadRedactor.Redact(amendment.ChangesJson)!;
            amendment.SourceSnapshotJson = RetainedOrderPayloadRedactor.Redact(amendment.SourceSnapshotJson)!;
            amendment.SupplementSnapshotJson = RetainedOrderPayloadRedactor.Redact(amendment.SupplementSnapshotJson);
            amendment.CommitResultJson = RetainedOrderPayloadRedactor.Redact(amendment.CommitResultJson);
            if (amendment.State == OrderAmendmentState.Quoted)
                amendment.ExpiresAt = now;
            amendment.UpdatedAt = now;
            amendment.UpdatedBy = Audit;
        }

        var notes = await context.OrderOperationalNotes
            .Where(value => orderIds.Contains(value.OrderId)).ToListAsync(cancellationToken);
        foreach (var note in notes)
        {
            note.Text = Erased;
            note.KitchenChangesJson = RetainedOrderPayloadRedactor.Redact(note.KitchenChangesJson);
            note.WithdrawnAt ??= now;
            note.UpdatedAt = now;
            note.UpdatedBy = Audit;
        }
        await context.OrderItems.Where(value => orderIds.Contains(value.OrderId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.SpecialInstructions, (string?)null), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}
