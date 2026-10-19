using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

public sealed class CustomerBasketOperationService(
    ApplicationDbContext context, IBasketService baskets, ICurrentUserService currentUser,
    CustomMediator mediator, IHttpContextAccessor httpContext) : ICustomerBasketOperationService
{
    public Task<BasketDto> AddAsync(string sessionId, AddToBasketDto item, Guid operationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(sessionId, operationId, CustomerBasketPayload.Add(item),
            () => baskets.AddItemToBasketAsync(sessionId, currentUser.UserId, item), cancellationToken);

    public Task<BasketDto> ExecuteAsync(string sessionId, Guid operationId, string payload,
        Func<Task<BasketDto>> apply, CancellationToken cancellationToken) =>
        RunAsync(sessionId, operationId, payload, async previous =>
        {
            if (previous is not null)
            {
                if (previous.IsCancelled) throw new ConflictException("This basket operation was cancelled. Refresh your basket.");
                return await ReplayAsync(previous, sessionId, cancellationToken)
                    ?? throw new ConflictException("This operation was already applied, but its basket is no longer available. Refresh your basket.");
            }
            // Accepted operations remain recoverable after native rollout is withdrawn.
            await CustomerBasketAvailability.EnsureOrderingAsync(mediator, cancellationToken);
            var basket = await apply();
            context.Set<CustomerBasketOperation>().Add(Receipt(sessionId, operationId, payload, basket.Id, false));
            return basket;
        }, cancellationToken);

    public Task<CustomerBasketOperationResolutionDto> CancelAddAsync(string sessionId, AddToBasketDto request,
        CancellationToken cancellationToken) =>
        CancelAsync(sessionId, request.OperationId ?? Guid.Empty, CustomerBasketPayload.Add(request), cancellationToken);

    public Task<CustomerBasketOperationResolutionDto> CancelMutationAsync(string sessionId, Guid basketItemId,
        CustomerBasketItemMutationDto request, CancellationToken cancellationToken) =>
        CancelAsync(sessionId, request.OperationId, CustomerBasketPayload.Mutation(basketItemId, request), cancellationToken);

    public Task<CustomerBasketOperationResolutionDto> CancelChannelAsync(string sessionId, CustomerBasketChannelDto request,
        CancellationToken cancellationToken) =>
        CancelAsync(sessionId, request.OperationId, CustomerBasketPayload.Channel(request), cancellationToken);

    private Task<CustomerBasketOperationResolutionDto> CancelAsync(string sessionId, Guid operationId,
        string payload, CancellationToken cancellationToken) =>
        RunAsync(sessionId, operationId, payload, async previous =>
        {
            if (previous is { IsCancelled: false })
                return new CustomerBasketOperationResolutionDto(operationId, CustomerBasketOperationOutcome.Applied,
                    await ReplayAsync(previous, sessionId, cancellationToken));
            if (previous is null)
                context.Set<CustomerBasketOperation>().Add(Receipt(sessionId, operationId, payload, Guid.Empty, true));
            // No new effect is applied, including when the original payload was refused or ordering
            // was disabled. The durable receipt seals late execution of this exact operation.
            return new CustomerBasketOperationResolutionDto(operationId, CustomerBasketOperationOutcome.Cancelled, null);
        }, cancellationToken);

    private async Task<T> RunAsync<T>(string sessionId, Guid operationId, string payload,
        Func<CustomerBasketOperation?, Task<T>> work, CancellationToken cancellationToken)
    {
        var session = CustomerBasketOperationHash.Session(sessionId);
        if (operationId == Guid.Empty) throw new BadRequestException("A non-empty operation ID is required.");
        CustomerBasketAccess.EnsureCustomerOrGuest(currentUser, httpContext.HttpContext);
        var owner = CustomerBasketOperationHash.Owner(session, currentUser.UserId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var operationLock = CustomerBasketOperationHash.OperationLock(operationId);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({operationLock})", cancellationToken);
        var ownerLock = CustomerBasketOperationHash.OwnerLock(session, currentUser.UserId);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({ownerLock})", cancellationToken);
        var previous = await context.Set<CustomerBasketOperation>().AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.OperationId == operationId, cancellationToken);
        if (previous is not null)
        {
            if (!string.Equals(previous.OwnerHash, owner, StringComparison.Ordinal))
                throw new NotFoundException("Basket operation unavailable.");
            if (!string.Equals(previous.PayloadHash, payload, StringComparison.Ordinal))
                throw new ConflictException("This operation ID was already used for different basket details.");
        }
        var result = await work(previous);
        if (previous is null) await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private CustomerBasketOperation Receipt(string sessionId, Guid operationId, string payload, Guid basketId, bool cancelled) => new()
    {
        OperationId = operationId,
        OwnerHash = CustomerBasketOperationHash.Owner(CustomerBasketOperationHash.Session(sessionId), currentUser.UserId),
        PayloadHash = payload,
        BasketId = basketId,
        IsCancelled = cancelled,
        AppliedAt = DateTime.UtcNow
    };

    private async Task<BasketDto?> ReplayAsync(CustomerBasketOperation operation, string sessionId,
        CancellationToken cancellationToken)
    {
        var basket = await baskets.GetBasketAsync(sessionId, currentUser.UserId);
        cancellationToken.ThrowIfCancellationRequested();
        return basket?.Id == operation.BasketId ? basket : null;
    }
}
