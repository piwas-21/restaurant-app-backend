using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed class OrderAmendmentQueryService : IOrderAmendmentQueryService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantFeatures _features;
    private readonly ICurrentUserService _currentUser;
    private readonly IOrderMappingService _mapping;

    public OrderAmendmentQueryService(
        ApplicationDbContext context,
        ITenantFeatures features,
        ICurrentUserService currentUser,
        IOrderMappingService mapping)
    {
        _context = context;
        _features = features;
        _currentUser = currentUser;
        _mapping = mapping;
    }

    public async Task<IReadOnlyList<OrderAmendmentHistoryDto>> ListAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        OrderAmendmentPolicy.RequireFeature(_features);
        OrderAmendmentPolicy.RequireActor(_currentUser);
        if (!await _context.Orders.AnyAsync(order => order.Id == orderId, cancellationToken))
            throw new NotFoundException("The source order was not found.");

        var amendments = await _context.Set<OrderAmendment>()
            .AsNoTracking()
            .Where(amendment => amendment.SourceOrderId == orderId)
            .OrderByDescending(amendment => amendment.CreatedAt)
            .ToListAsync(cancellationToken);
        var supplements = await LoadSupplementsAsync(amendments, cancellationToken);
        return amendments.Select(amendment => new OrderAmendmentHistoryDto
        {
            AmendmentId = amendment.Id,
            SourceOrderId = amendment.SourceOrderId,
            ServiceSessionId = amendment.ServiceSessionId,
            SupplementOrderId = amendment.SupplementOrderId,
            ActorRole = amendment.ActorRole,
            State = amendment.State.ToString(),
            CreatedAt = amendment.CreatedAt,
            CommittedAt = amendment.CommittedAt,
            SupplementOrder = amendment.SupplementOrderId is Guid supplementId
                ? supplements.GetValueOrDefault(supplementId) : null,
            Changes = OrderAmendmentResponseRedactor.RedactChanges(
                OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson)),
            FinancialResolution = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(
                amendment.FinancialResolutionJson)
        }).ToList();
    }

    public async Task<OrderAmendmentOperationLookupDto> LookupAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        // Read-only recovery remains available after operators disable new amendments.
        var actorId = OrderAmendmentPolicy.RequireActor(_currentUser);
        var amendment = await _context.Set<OrderAmendment>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ActorUserId == actorId
                && candidate.ClientOperationId == operationId, cancellationToken);
        if (amendment is null || amendment.State != Domain.Common.Enums.OrderAmendmentState.Committed)
        {
            return new OrderAmendmentOperationLookupDto(
                operationId, OrderAmendmentOperationStatus.Unknown, null);
        }

        var result = string.IsNullOrWhiteSpace(amendment.CommitResultJson)
            ? await MaterializeCommittedResultAsync(amendment, cancellationToken)
            : OrderAmendmentJson.Deserialize<OrderAmendmentCommitDto>(amendment.CommitResultJson);
        return new OrderAmendmentOperationLookupDto(
            operationId, OrderAmendmentOperationStatus.Committed,
            OrderAmendmentResponseRedactor.RedactCommit(result));
    }

    public async Task<OrderAmendmentCommitDto> MaterializeCommittedResultAsync(
        OrderAmendment amendment, CancellationToken cancellationToken)
    {
        OrderDto? supplement = null;
        if (amendment.SupplementOrderId is Guid supplementId)
        {
            var order = await LoadSupplementAsync(supplementId, cancellationToken);
            if (order is not null)
                supplement = OrderAmendmentResponseRedactor.RedactOrder(
                    _mapping.MapToOrderDto(order));
        }

        return OrderAmendmentResponseRedactor.RedactCommit(new OrderAmendmentCommitDto
        {
            AmendmentId = amendment.Id,
            ClientOperationId = amendment.ClientOperationId
                ?? throw new ConflictException("The committed amendment has no operation id."),
            SourceOrderId = amendment.SourceOrderId,
            SupplementOrderId = amendment.SupplementOrderId,
            CommittedAccountRevision = amendment.CommittedAccountRevision,
            CommittedAt = amendment.CommittedAt ?? amendment.CreatedAt,
            FinancialResolution = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(
                amendment.FinancialResolutionJson),
            SupplementOrder = supplement
        });
    }

    private async Task<Dictionary<Guid, OrderDto>> LoadSupplementsAsync(
        IReadOnlyCollection<OrderAmendment> amendments, CancellationToken cancellationToken)
    {
        var ids = amendments.Where(amendment => amendment.SupplementOrderId.HasValue)
            .Select(amendment => amendment.SupplementOrderId!.Value).Distinct().ToArray();
        if (ids.Length == 0)
            return [];

        var orders = await _context.Orders
            .Where(order => ids.Contains(order.Id) && !order.IsDeleted)
            .IncludeOrderLineGraph()
            .Include(order => order.Payments)
            .Include(order => order.ExternalReference)
            .Include(order => order.RoutingStates)
            .ToListAsync(cancellationToken);
        return orders.ToDictionary(
            order => order.Id,
            order => OrderAmendmentResponseRedactor.RedactOrder(_mapping.MapToOrderDto(order)));
    }

    private async Task<Order?> LoadSupplementAsync(Guid orderId, CancellationToken cancellationToken) =>
        await _context.Orders
            .Where(order => order.Id == orderId && !order.IsDeleted)
            .IncludeOrderLineGraph()
            .Include(order => order.Payments)
            .Include(order => order.ExternalReference)
            .Include(order => order.RoutingStates)
            .SingleOrDefaultAsync(cancellationToken);
}
