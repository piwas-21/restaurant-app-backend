using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCurrentStaffTicketQuery;

public sealed record GetCurrentStaffTicketQuery(Guid OrderId) : IQuery<ApiResponse<IReadOnlyList<OrderDto>>>;

public sealed class GetCurrentStaffTicketQueryHandler(
    ApplicationDbContext context, ICurrentUserService currentUser, IOrderQueueProjection projection,
    IOptions<CurrentStaffTicketSettings> settings)
    : IQueryHandler<GetCurrentStaffTicketQuery, ApiResponse<IReadOnlyList<OrderDto>>>
{
    public async Task<ApiResponse<IReadOnlyList<OrderDto>>> Handle(
        GetCurrentStaffTicketQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsStaff) throw new ForbiddenException("The current ticket is available only to staff.");
        await using var snapshot = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var source = await context.Orders.AsNoTracking().Where(order => order.Id == request.OrderId)
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
            .Select(amendment => new { amendment.SourceOrderId, amendment.ChangesJson })
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
        await snapshot.CommitAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<OrderDto>>.SuccessWithData(results);
    }

    private static ConflictException Oversized() =>
        new("This ticket is too large to display safely. Ask an administrator for help.");
}
