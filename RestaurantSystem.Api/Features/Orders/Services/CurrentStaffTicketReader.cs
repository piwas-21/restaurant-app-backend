using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>One consistent current-ticket projection shared by staff screens and cashier snapshots.</summary>
public sealed class CurrentStaffTicketReader(ApplicationDbContext context, IOrderQueueProjection projection,
    IOptions<CurrentStaffTicketSettings> settings) : ICurrentStaffTicketReader
{
    public async Task<IReadOnlyList<OrderDto>> ReadAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var snapshot = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var source = await context.Orders.AsNoTracking().Where(order => order.Id == orderId)
            .Select(order => new { order.ServiceSessionId, RootId = order.AmendmentRootOrderId ?? order.Id })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The source order was not found.");
        var members = context.Orders.AsNoTracking();
        members = source.ServiceSessionId is Guid sessionId
            ? members.Where(order => order.ServiceSessionId == sessionId)
            : members.Where(order => !order.ServiceSessionId.HasValue
                && (order.AmendmentRootOrderId ?? order.Id) == source.RootId);
        var ids = await members.OrderBy(order => order.OrderDate).ThenBy(order => order.Id)
            .Select(order => order.Id).Take(settings.Value.MaximumMembers + 1)
            .ToArrayAsync(cancellationToken);
        if (ids.Length > settings.Value.MaximumMembers) throw Oversized();
        var amendments = await context.OrderAmendments.AsNoTracking()
            .Where(amendment => ids.Contains(amendment.SourceOrderId)
                && amendment.State == OrderAmendmentState.Committed)
            .OrderBy(amendment => amendment.CommittedAt).ThenBy(amendment => amendment.CreatedAt)
            .ThenBy(amendment => amendment.Id)
            .Select(amendment => new OrderAmendment
            {
                Id = amendment.Id,
                CreatedBy = amendment.CreatedBy,
                SourceOrderId = amendment.SourceOrderId,
                State = amendment.State,
                ChangesJson = amendment.ChangesJson,
                FinancialResolutionJson = amendment.FinancialResolutionJson
            })
            .Take(settings.Value.MaximumAmendments + 1)
            .ToListAsync(cancellationToken);
        if (amendments.Count > settings.Value.MaximumAmendments) throw Oversized();
        var orders = await context.Orders.AsNoTracking().Where(order => ids.Contains(order.Id))
            .IncludeOrderLineGraph()
            .Include(order => order.Items).ThenInclude(item => item.ProductVariation)
            .Include(order => order.Table)
            .Include(order => order.Payments).Include(order => order.ExternalReference)
            .Include(order => order.RoutingStates)
            .AsSplitQuery()
            .OrderBy(order => order.OrderDate).ThenBy(order => order.Id).ToListAsync(cancellationToken);
        var changes = amendments.ToLookup(amendment => amendment.SourceOrderId);
        var results = new List<OrderDto>(orders.Count);
        foreach (var order in orders)
        {
            var dto = projection.Project(order, includePermittedActions: true);
            // This staff-only read owns operational text; the audit endpoint stays redacted.
            var lineChanges = changes[order.Id].SelectMany(amendment => OrderAmendmentJson
                .Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson)).ToArray();
            dto.Items = CurrentStaffTicketItems.Project(dto.Items, lineChanges, dto.Currency);
            CurrentStaffTicketAmounts.Apply(order, dto, changes[order.Id].ToArray());
            results.Add(dto);
        }
        if (snapshot is not null) await snapshot.CommitAsync(cancellationToken);
        return results;
    }

    private static ConflictException Oversized() =>
        new("This ticket is too large to display safely. Ask an administrator for help.");
}
