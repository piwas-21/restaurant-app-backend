using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

public sealed class CustomerBasketOperationService(
    ApplicationDbContext context, IBasketService baskets, ICurrentUserService currentUser,
    CustomMediator mediator, IHttpContextAccessor httpContext) : ICustomerBasketOperationService
{
    public async Task<BasketDto> AddAsync(string sessionId, AddToBasketDto item, Guid operationId,
        CancellationToken cancellationToken)
    {
        var session = CustomerBasketOperationHash.Session(sessionId);
        if (operationId == Guid.Empty) throw new BadRequestException("A non-empty operation ID is required.");
        if (currentUser.IsApiToken || currentUser.IsAuthenticated && currentUser.Role != UserRole.Customer)
            throw new ForbiddenException("Customer basket operations require a customer or guest session.");
        if (!currentUser.IsAuthenticated && httpContext.HttpContext?.Request.Headers.ContainsKey(HeaderNames.Authorization) == true)
            throw new UnauthorizedAccessException("Renew customer authentication before retrying this basket operation.");
        var owner = CustomerBasketOperationHash.Owner(session, currentUser.UserId);
        var payload = CustomerBasketOperationHash.Payload(item);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var operationLock = CustomerBasketOperationHash.OperationLock(operationId);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({operationLock})", cancellationToken);
        var ownerLock = CustomerBasketOperationHash.OwnerLock(session, currentUser.UserId);
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({ownerLock})", cancellationToken);
        var previous = await context.Set<CustomerBasketOperation>().AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.OperationId == operationId, cancellationToken);
        if (previous is not null)
        {
            var replay = await ReplayAsync(previous, owner, payload, sessionId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        // Previously accepted operations remain recoverable after native rollout is withdrawn.
        var bootstrap = await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken);
        if (bootstrap.Data?.Capabilities.GetValueOrDefault("ordering") != "enabled")
            throw new NotFoundException("Customer mobile ordering is unavailable.");
        var basket = await baskets.AddItemToBasketAsync(sessionId, currentUser.UserId, item);
        context.Set<CustomerBasketOperation>().Add(new CustomerBasketOperation
        {
            OperationId = operationId,
            OwnerHash = owner,
            PayloadHash = payload,
            BasketId = basket.Id,
            AppliedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return basket;
    }

    private async Task<BasketDto> ReplayAsync(CustomerBasketOperation operation, string owner, string payload,
        string sessionId, CancellationToken cancellationToken)
    {
        if (!string.Equals(operation.OwnerHash, owner, StringComparison.Ordinal))
            throw new NotFoundException("Basket operation unavailable.");
        if (!string.Equals(operation.PayloadHash, payload, StringComparison.Ordinal))
            throw new ConflictException("This operation ID was already used for different basket details.");
        var basket = await baskets.GetBasketAsync(sessionId, currentUser.UserId);
        if (basket is null || basket.Id != operation.BasketId)
            throw new ConflictException("This item was already added, but its basket is no longer available. Refresh your basket.");
        cancellationToken.ThrowIfCancellationRequested();
        return basket;
    }
}
