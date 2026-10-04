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
    private readonly OrderAmendmentCommitMaterializer _materializer;
    private readonly OrderAmendmentCommitWriter _writer;
    private readonly IOrderAmendmentQueryService _queries;
    private readonly IOrderNotificationService _notifications;

    public OrderAmendmentCommitService(
        ApplicationDbContext context, ITenantFeatures features, ICurrentUserService currentUser,
        OrderAmendmentCommitMaterializer materializer, OrderAmendmentCommitWriter writer,
        IOrderAmendmentQueryService queries, IOrderNotificationService notifications)
    {
        _context = context;
        _features = features;
        _currentUser = currentUser;
        _materializer = materializer;
        _writer = writer;
        _queries = queries;
        _notifications = notifications;
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
            ValidateQuoteOwner(amendment, actorId);
            if (amendment.State != OrderAmendmentState.Quoted)
                return await ReplayCommittedAsync(amendment, request, payloadHash, cancellationToken);
            ValidateQuoteVersions(amendment, request);

            var source = await OrderAmendmentOrderLoader.LoadSourceAsync(_context, orderId, cancellationToken)
                ?? throw new NotFoundException("The source order was not found.");
            await OrderAmendmentFinancialGuard.AssertNoPendingSourceResolutionAsync(
                _context, source.Id, cancellationToken);
            var prepared = await _materializer.PrepareAsync(source, amendment, request, _currentUser, cancellationToken);
            if (prepared.Request.Additions.Count > 0 || prepared.Changes.Any(change =>
                    change.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace))
                scope.RecordAccountChange();
            var accountRevision = source.ServiceSession?.AccountRevision;
            if (accountRevision <= 0)
                accountRevision = null;

            var committedResult = await _writer.StageAsync(
                source, amendment, request, prepared, payloadHash, accountRevision, cancellationToken);
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

    private static void ValidateQuoteOwner(OrderAmendment amendment, Guid actorId)
    {
        if (amendment.ActorUserId != actorId)
            throw new ForbiddenException("This amendment quote belongs to another staff member.");
    }

    private static void ValidateQuoteVersions(OrderAmendment amendment, OrderAmendmentCommitRequest request)
    {
        if (amendment.ExpiresAt <= DateTime.UtcNow)
            throw new ConflictException("This amendment quote expired. Quote the order again.");
        if (amendment.ClientOperationId.HasValue && amendment.ClientOperationId != request.ClientOperationId)
            throw new ConflictException("This quote is already linked to another commit operation.");
        if (request.ExpectedOrderVersion != amendment.ExpectedOrderVersion
            || request.ExpectedAccountRevision != amendment.ExpectedAccountRevision)
            throw new ConflictException("The commit does not match the versions that were quoted.");
    }

    private async Task<CommitOutcome> ReplayCommittedAsync(
        OrderAmendment amendment, OrderAmendmentCommitRequest request, string payloadHash,
        CancellationToken cancellationToken)
    {
        if (amendment.State != OrderAmendmentState.Committed
            || amendment.ClientOperationId != request.ClientOperationId || amendment.CommitPayloadHash != payloadHash)
            throw new ConflictException("This quote has already been committed. Refresh the order.");
        var replay = (await _queries.LookupAsync(request.ClientOperationId, cancellationToken)).Result
            ?? throw new ConflictException("The committed amendment result could not be loaded.");
        return new CommitOutcome(replay, IsNewCommit: false);
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

    private sealed record CommitOutcome(OrderAmendmentCommitDto Result, bool IsNewCommit);
}
