using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed class OrderAmendmentCommitService : IOrderAmendmentCommitService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantFeatures _features;
    private readonly ICurrentUserService _currentUser;
    private readonly IOrderAmendmentReservationGuard _reservationGuard;
    private readonly IOrderAmendmentFinancialResolution _financial;
    private readonly OrderAmendmentSupplementBuilder _supplements;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly IStaffCounterOrderPricing _serverPricing;
    private readonly IOrderItemFactory _itemFactory;
    private readonly IOrderMappingService _mapping;
    private readonly IOrderRoutingService _routing;
    private readonly IOrderAmendmentQueryService _queries;
    private readonly IOrderNotificationService _notifications;
    private readonly OrderAmendmentKitchenStager _kitchenStager;

    public OrderAmendmentCommitService(
        ApplicationDbContext context,
        ITenantFeatures features,
        ICurrentUserService currentUser,
        IOrderAmendmentReservationGuard reservationGuard,
        IOrderAmendmentFinancialResolution financial,
        OrderAmendmentSupplementBuilder supplements,
        IOrderFidelityCoordinator fidelity,
        IStaffCounterOrderPricing serverPricing,
        IOrderItemFactory itemFactory,
        IOrderMappingService mapping,
        IOrderRoutingService routing,
        IOrderAmendmentQueryService queries,
        IOrderNotificationService notifications,
        OrderAmendmentKitchenStager kitchenStager)
    {
        _context = context;
        _features = features;
        _currentUser = currentUser;
        _reservationGuard = reservationGuard;
        _financial = financial;
        _supplements = supplements;
        _fidelity = fidelity;
        _serverPricing = serverPricing;
        _itemFactory = itemFactory;
        _mapping = mapping;
        _routing = routing;
        _queries = queries;
        _notifications = notifications;
        _kitchenStager = kitchenStager;
    }

    public async Task<OrderAmendmentCommitDto> CommitAsync(
        Guid orderId, OrderAmendmentCommitRequest request, CancellationToken cancellationToken)
    {
        OrderAmendmentPolicy.RequireFeature(_features);
        var actorId = OrderAmendmentPolicy.RequireActor(_currentUser);
        if (!request.ReviewAcknowledged)
            throw new BadRequestException("Review the amendment quote before committing it.");

        var payloadHash = PayloadHash(orderId, request);
        var replay = await FindReplayAsync(
            actorId, orderId, request.ClientOperationId, payloadHash, cancellationToken);
        if (replay is not null)
            return replay;

        try
        {
            var committed = await CommitLockedAsync(orderId, request, actorId, payloadHash, cancellationToken);
            if (committed.IsNewCommit && committed.Result.SupplementOrder is not null)
            {
                await _notifications.NotifyOrderCreatedAsync(committed.Result.SupplementOrder);
            }

            return committed.Result;
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            replay = await FindReplayAsync(
                actorId, orderId, request.ClientOperationId, payloadHash, cancellationToken);
            if (replay is not null)
                return replay;
            throw;
        }
    }

    private async Task<CommitOutcome> CommitLockedAsync(
        Guid orderId,
        OrderAmendmentCommitRequest request,
        Guid actorId,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var scope = await OrderAccountMutationScope.BeginAsync(
            _context, orderId, cancellationToken);
        try
        {
            var amendment = await _context.Set<OrderAmendment>()
                .SingleOrDefaultAsync(candidate => candidate.Id == request.AmendmentId
                    && candidate.SourceOrderId == orderId, cancellationToken)
                ?? throw new NotFoundException("The amendment quote was not found.");
            if (amendment.ActorUserId != actorId)
                throw new ForbiddenException("This amendment quote belongs to another staff member.");
            if (amendment.State != OrderAmendmentState.Quoted)
            {
                if (amendment.State == OrderAmendmentState.Committed
                    && amendment.ClientOperationId == request.ClientOperationId
                    && amendment.CommitPayloadHash == payloadHash)
                {
                    var replay = (await _queries.LookupAsync(request.ClientOperationId, cancellationToken)).Result
                        ?? throw new ConflictException("The committed amendment result could not be loaded.");
                    return new CommitOutcome(replay, IsNewCommit: false);
                }
                throw new ConflictException("This quote has already been committed. Refresh the order.");
            }

            if (amendment.ExpiresAt <= DateTime.UtcNow)
                throw new ConflictException("This amendment quote expired. Quote the order again.");
            if (amendment.ClientOperationId.HasValue && amendment.ClientOperationId != request.ClientOperationId)
                throw new ConflictException("This quote is already linked to another commit operation.");
            if (request.ExpectedOrderVersion != amendment.ExpectedOrderVersion
                || request.ExpectedAccountRevision != amendment.ExpectedAccountRevision)
            {
                throw new ConflictException("The commit does not match the versions that were quoted.");
            }

            var source = await OrderAmendmentOrderLoader.LoadSourceAsync(_context, orderId, cancellationToken)
                ?? throw new NotFoundException("The source order was not found.");
            var quoteRequest = OrderAmendmentJson.Deserialize<OrderAmendmentQuoteRequest>(amendment.RequestJson);
            OrderAmendmentPolicy.ValidateOrderContext(source, quoteRequest);
            OrderAmendmentPolicy.ValidateChangeAuthority(source, quoteRequest, _currentUser);
            if (source.Version != request.ExpectedOrderVersion
                || source.Version != amendment.ExpectedOrderVersion)
            {
                throw new ConflictException("The source order changed after the quote. Quote again.");
            }

            var sourceDto = _mapping.MapToOrderDto(source);
            var sourceQuantities = source.Items.Where(item => !item.ParentOrderItemId.HasValue)
                .ToDictionary(item => item.Id, item => item.Quantity);
            await OrderAmendmentRangeValidator.ValidateAsync(
                _context, source.Id, quoteRequest, sourceQuantities, cancellationToken);

            var scopes = DeserializeChanges(amendment.ChangesJson).Select(change => change.Kind switch
            {
                OrderAmendmentChangeKind.InstructionChange => new OrderAmendmentUnitScope(
                    change.OrderItemId, 0, 0, WholeLine: true),
                _ => new OrderAmendmentUnitScope(
                    change.OrderItemId, change.StartOrdinal, change.Quantity, WholeLine: false)
            }).ToList();
            await _reservationGuard.AssertUnitsMutableAsync(source.Id, scopes, cancellationToken);

            var supplement = await _supplements.BuildAsync(source, quoteRequest, cancellationToken);
            var snapshot = amendment.SupplementSnapshotJson is null
                ? null
                : OrderAmendmentJson.Deserialize<OrderAmendmentSupplementSnapshot>(amendment.SupplementSnapshotJson);
            if (supplement is not null && snapshot is not null)
            {
                OrderAmendmentSnapshots.RestoreSupplementIdentity(supplement, snapshot);
            }

            var supplementDto = supplement is null ? null : _mapping.MapToOrderDto(supplement);
            if ((supplement is null) != (snapshot is null)
                || (supplementDto is not null && snapshot is not null
                    && !string.Equals(snapshot.PricingFingerprint,
                        OrderAmendmentJson.PricingFingerprint(supplementDto), StringComparison.Ordinal)))
            {
                throw new ConflictException("The supplement price changed after the quote. Quote the order again.");
            }

            var changes = await OrderAmendmentChangeBuilder.BuildAsync(
                source, sourceDto, quoteRequest, supplementDto, _serverPricing,
                _itemFactory, _mapping, cancellationToken);
            var quotedChanges = DeserializeChanges(amendment.ChangesJson);
            if (!string.Equals(
                    OrderAmendmentJson.ChangeFingerprint(changes),
                    OrderAmendmentJson.ChangeFingerprint(quotedChanges),
                    StringComparison.Ordinal))
            {
                throw new ConflictException("The source-line snapshot changed after the quote. Quote again.");
            }

            var financial = await _financial.PreviewAsync(source, changes, supplement, cancellationToken);
            var quotedFinancial = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(
                amendment.FinancialResolutionJson);
            if (financial != quotedFinancial)
                throw new ConflictException("The financial preview changed after the quote. Quote again.");

            var changesAccount = quoteRequest.Additions.Count > 0
                || changes.Any(change => change.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace);
            if (changesAccount)
                scope.RecordAccountChange();
            var accountRevision = source.ServiceSession?.AccountRevision;
            if (accountRevision <= 0)
                accountRevision = null;

            if (supplement is not null)
            {
                amendment.SupplementOrderId = supplement.Id;
                _context.Orders.Add(supplement);
                if (supplement.IsKitchenReleased)
                    await _routing.EnsureRoutesAsync(supplement, cancellationToken);
            }

            amendment.ChangesJson = OrderAmendmentJson.Serialize(changes);
            AddSourceAuditNote(source, amendment, quoteRequest.Reason);
            amendment.ClientOperationId = request.ClientOperationId;
            amendment.CommitPayloadHash = payloadHash;
            amendment.CommittedAccountRevision = accountRevision;
            amendment.CommittedAt = DateTime.UtcNow;
            amendment.State = OrderAmendmentState.Committed;
            await _financial.StageAsync(amendment, source, financial, cancellationToken);
            await _kitchenStager.StageAsync(
                source, amendment.Id, accountRevision, changes, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            if (supplement is not null && quoteRequest.PointsToRedeem is > 0)
            {
                await _fidelity.RedeemAsync(
                    supplement, quoteRequest.PointsToRedeem, supplement.UserId,
                    cancellationToken, failOnError: true);
            }

            var committedResult = await _queries.MaterializeCommittedResultAsync(amendment, cancellationToken);
            amendment.CommitResultJson = OrderAmendmentJson.Serialize(committedResult);
            await _context.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return new CommitOutcome(committedResult, IsNewCommit: true);
        }
        catch
        {
            if (_context.Database.CurrentTransaction is not null)
                await _context.Database.CurrentTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private void AddSourceAuditNote(
        Order source, OrderAmendment amendment, string? reason)
    {
        var note = string.IsNullOrWhiteSpace(reason)
            ? $"Order amendment {amendment.Id:N} committed."
            : $"Order amendment {amendment.Id:N}: {OrderAmendmentPolicy.SanitizeText(reason, 350)}";
        _context.OrderOperationalNotes.Add(new OrderOperationalNote
        {
            OrderId = source.Id,
            Order = source,
            ClientOperationId = amendment.Id,
            Audience = OrderNoteAudience.Staff,
            Text = note,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.GetAuditIdentifier()
        });
    }

    private async Task<OrderAmendmentCommitDto?> FindReplayAsync(
        Guid actorId, Guid sourceOrderId, Guid operationId, string payloadHash,
        CancellationToken cancellationToken)
    {
        var existing = await _context.Set<OrderAmendment>().AsNoTracking()
            .SingleOrDefaultAsync(amendment => amendment.ActorUserId == actorId
                && amendment.ClientOperationId == operationId, cancellationToken);
        if (existing is null)
            return null;
        if (existing.SourceOrderId != sourceOrderId)
        {
            throw new ConflictException("This operation id belongs to a different source order.");
        }

        if (existing.State != OrderAmendmentState.Committed
            || existing.CommitPayloadHash != payloadHash)
        {
            throw new ConflictException("This operation id was already used with a different amendment request.");
        }

        return (await _queries.LookupAsync(operationId, cancellationToken)).Result
            ?? throw new ConflictException("The committed amendment result could not be loaded.");
    }

    private static string PayloadHash(Guid orderId, OrderAmendmentCommitRequest request) =>
        OrderAmendmentJson.Hash($"{orderId:N}:{OrderAmendmentJson.Serialize(request)}");

    private static List<OrderAmendmentChangeSnapshot> DeserializeChanges(string json) =>
        OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(json);

    private sealed record CommitOutcome(OrderAmendmentCommitDto Result, bool IsNewCommit);
}
