using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

/// <summary>Records staff confirmation of real-world work without inventing printer receipts.</summary>
internal static class StaffHandoverAcknowledgement
{
    internal static async Task RecordAsync(ApplicationDbContext context, Order order,
        string actor, CancellationToken cancellationToken)
    {
        var corrections = await KitchenBoardWorkRules.ActiveCorrections(context)
            .Where(note => note.OrderId == order.Id).ToListAsync(cancellationToken);
        var acknowledged = await context.KitchenBoardWorkCompletions.AnyAsync(work =>
            work.OrderId == order.Id && work.Kind == KitchenBoardWorkKind.StaffHandover,
            cancellationToken);
        if (!acknowledged)
            context.KitchenBoardWorkCompletions.Add(Create(order, order.Id,
                KitchenBoardWorkKind.StaffHandover, null, actor));
        foreach (var note in corrections)
        {
            context.KitchenBoardWorkCompletions.Add(Create(order, note.Id,
                KitchenBoardWorkKind.AmendmentCorrection, note.AccountRevision, actor));
        }
        await context.SaveChangesAsync(cancellationToken);
        foreach (var note in corrections.Where(note => note.KitchenTarget.HasValue))
            await KitchenBoardSequenceWriter.TouchCorrectionAsync(context, order.Id, note.Id,
                note.KitchenTarget!.Value, cancellationToken);
    }

    private static KitchenBoardWorkCompletion Create(Order order, Guid workId,
        KitchenBoardWorkKind kind, long? revision, string actor) => new()
        {
            OrderId = order.Id,
            WorkItemId = workId,
            Kind = kind,
            AcknowledgedOrderVersion = order.Version,
            AccountRevision = revision,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actor,
        };
}
