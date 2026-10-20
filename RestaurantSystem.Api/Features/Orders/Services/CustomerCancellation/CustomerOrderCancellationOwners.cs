using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.User.Services;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

/// <summary>Resolves request authority before each short locked cancellation phase.</summary>
public sealed class CustomerOrderCancellationOwners(ICurrentUserService currentUser,
    ICustomerCheckoutGuard guard, ICustomerOrderOperationStore operations) : ICustomerOrderCancellationOwners
{
    public CustomerOrderCancellationOwner Customer(Guid orderId)
    {
        var owner = CustomerAccountAccess.RequireOwner(currentUser);
        if (orderId == Guid.Empty) throw new BadRequestException("An order is required.");
        return new(orderId, owner, CustomerOrderCancellationHash.Owner(owner, Guid.Empty));
    }

    public async Task<CustomerOrderCancellationOwner> OriginalAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken)
    {
        // Failed authentication cannot silently become an anonymous original-order capability.
        var userId = guard.EnsureOwner(sessionId);
        if (proof is null || proof.SchemaVersion != 1 || proof.OperationId == Guid.Empty
            || string.IsNullOrEmpty(proof.PayloadHash))
            throw new BadRequestException("A supported original checkout proof is required.");
        var operation = await operations.RecoverAsync(sessionId, proof.OperationId, proof.PayloadHash, cancellationToken);
        if (operation.Outcome != CustomerOrderOperationOutcome.Applied || operation.OrderId is not { } orderId || orderId == Guid.Empty)
            throw new NotFoundException("Order cancellation unavailable.");
        return new(orderId, userId,
            CustomerOrderCancellationHash.Owner(userId, CustomerBasketOperationHash.Session(sessionId)));
    }
}
