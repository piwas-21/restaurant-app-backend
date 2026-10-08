using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService(
    ApplicationDbContext context,
    ICurrentUserService currentUser,
    ITenantFeatures features,
    IAccountCheckoutEvidenceReader checkoutEvidence,
    IOrderAmendmentRefundProvider refundProvider,
    IOrderAmendmentResolutionPolicy resolutionPolicy,
    IOrderAmendmentFinancialResolution financialResolution,
    IOrderAmendmentResolutionFinalizer finalizer)
    : IOrderAmendmentResolutionService
{
    public async Task<OrderAmendmentResolutionQuoteDto> QuoteAsync(
        Guid orderId, Guid amendmentId, OrderAmendmentResolutionQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = RequireAdminActor();
        OrderAmendmentPolicy.RequireFeature(features);
        ValidateQuoteRequest(request);
        var state = await ReadPlanningStateAsync(orderId, amendmentId, request, cancellationToken);
        var expiresAt = resolutionPolicy.UtcNow.Add(resolutionPolicy.QuoteLifetime);
        return OrderAmendmentResolutionQuoteFactory.Create(
            orderId, amendmentId, actorId, request, state.Plan, expiresAt);
    }

    public async Task<OrderAmendmentResolutionStartOutcomeDto> StartAsync(
        Guid orderId, Guid amendmentId, OrderAmendmentResolutionStartRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = RequireAdminActor();
        ValidateStartRequest(request);
        request = NormalizeStartRequest(request);
        var requestHash = OrderAmendmentResolutionFingerprint.RequestHash(
            actorId, orderId, amendmentId, request);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await OrderAmendmentResolutionClientKeyLock.AcquireAsync(context, actorId,
            request.Quote.ClientOperationId, cancellationToken);
        var existing = await FindOperationByClientKeyAsync(actorId, request.Quote.ClientOperationId,
            cancellationToken);
        var refusal = await FindRefusalByClientKeyAsync(actorId, request.Quote.ClientOperationId,
            cancellationToken);
        if (existing is not null && refusal is not null)
            throw new ConflictException("The operation key has conflicting durable records.");
        if (existing is not null)
        {
            RequireSameRequest(existing, orderId, amendmentId, requestHash);
            await transaction.CommitAsync(cancellationToken);
            return OrderAmendmentResolutionStartOutcomeDto.Accepted(
                await ProcessAndReadAsync(existing.Id, cancellationToken));
        }
        if (refusal is not null)
        {
            RequireSameRefusal(refusal, orderId, amendmentId, requestHash);
            await transaction.CommitAsync(cancellationToken);
            return OrderAmendmentResolutionStartOutcomeDto.Refused(MapRefusal(refusal));
        }
        OrderAmendmentPolicy.RequireFeature(features);
        var decision = await StartNewUnderLocksAsync(orderId, amendmentId, request,
            requestHash, actorId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (decision.OperationId is Guid operationId)
            return OrderAmendmentResolutionStartOutcomeDto.Accepted(
                await ProcessAndReadAsync(operationId, cancellationToken));
        return OrderAmendmentResolutionStartOutcomeDto.Refused(decision.Refusal
            ?? throw new ConflictException("The durable refusal receipt is unavailable."));
    }

    public async Task<OrderAmendmentResolutionResultDto> LookupAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        var actorId = RequireAdminActor();
        if (!await context.OrderAmendmentResolutionOperations.AsNoTracking()
                .AnyAsync(value => value.Id == operationId && value.ActorUserId == actorId, cancellationToken))
            throw Unavailable();
        return await ReadResultAsync(operationId, cancellationToken);
    }

    public async Task<OrderAmendmentResolutionStartOutcomeDto> LookupByClientKeyAsync(
        Guid orderId, Guid amendmentId, Guid clientOperationId, CancellationToken cancellationToken)
    {
        var actorId = RequireAdminActor();
        if (orderId == Guid.Empty || amendmentId == Guid.Empty || clientOperationId == Guid.Empty)
            throw Unavailable();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await OrderAmendmentResolutionClientKeyLock.AcquireAsync(context, actorId,
            clientOperationId, cancellationToken);
        var operation = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Include(value => value.Legs)
            .SingleOrDefaultAsync(value => value.ClientOperationId == clientOperationId
                && value.ActorUserId == actorId, cancellationToken);
        var refusal = await FindRefusalByClientKeyAsync(actorId, clientOperationId, cancellationToken);
        if (operation is not null && refusal is not null)
            throw Unavailable();
        if (operation is not null)
        {
            if (operation.SourceOrderId != orderId || operation.AmendmentId != amendmentId)
                throw Unavailable();
            await transaction.CommitAsync(cancellationToken);
            return OrderAmendmentResolutionStartOutcomeDto.Accepted(
                await ReadResultAsync(operation.Id, cancellationToken));
        }
        if (refusal is null || refusal.SourceOrderId != orderId || refusal.AmendmentId != amendmentId)
            throw Unavailable();
        await transaction.CommitAsync(cancellationToken);
        return OrderAmendmentResolutionStartOutcomeDto.Refused(MapRefusal(refusal));
    }

    public async Task<OrderAmendmentResolutionResultDto> RecoverAsync(
        Guid operationId, CancellationToken cancellationToken)
    {
        RequireAdminActor();
        return await ProcessAndReadAsync(operationId, cancellationToken);
    }

    private Guid RequireAdminActor()
    {
        if (!currentUser.IsAuthenticated || currentUser.IsApiToken
            || currentUser.Role != UserRole.Admin || currentUser.UserId is not Guid actorId)
            throw new ForbiddenException("Only a signed-in administrator may settle paid amendment credits.");
        return actorId;
    }

    private static void ValidateQuoteRequest(OrderAmendmentResolutionQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ClientOperationId == Guid.Empty || request.ExpectedOrderVersion <= 0
            || string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Length != 3
            || request.Currency.Any(value => !char.IsAsciiLetter(value))
            || request.ManualRefunds is null || request.ManualRefunds.Any(value => value is null))
            throw new BadRequestException("A valid operation, source version and currency are required.");
    }

    private static void ValidateStartRequest(OrderAmendmentResolutionStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Quote);
        ValidateQuoteRequest(request.Quote);
        if (string.IsNullOrWhiteSpace(request.QuoteHash) || request.QuoteHash.Length != 64
            || request.ExpiresAt.Kind != DateTimeKind.Utc)
            throw new BadRequestException("The reviewed financial resolution quote is invalid.");
    }

    private static void RequireSameRequest(OrderAmendmentResolutionOperation operation,
        Guid orderId, Guid amendmentId, string requestHash)
    {
        if (operation.SourceOrderId != orderId || operation.AmendmentId != amendmentId
            || operation.RequestHash != requestHash)
            throw new ConflictException("This operation key is already bound to another amendment request.");
    }

    private static NotFoundException Unavailable() =>
        new("The amendment resolution operation is unavailable.");
}
