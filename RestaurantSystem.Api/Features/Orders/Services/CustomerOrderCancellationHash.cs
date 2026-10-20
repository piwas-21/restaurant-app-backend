using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class CustomerOrderCancellationHash
{
    public static string Payload(Guid orderId, int expectedVersion)
    {
        if (orderId == Guid.Empty || expectedVersion <= 0 || expectedVersion == int.MaxValue)
            throw new BadRequestException("A current order and version are required for cancellation.");
        return CustomerBasketOperationHash.Payload(new
        {
            SchemaVersion = 1,
            Kind = "DeclinePreparationDelay",
            OrderId = orderId,
            ExpectedVersion = expectedVersion
        });
    }

    public static string Owner(Guid? userId, Guid session)
    {
        if (userId == Guid.Empty || (!userId.HasValue && session == Guid.Empty))
            throw new BadRequestException("A verified cancellation owner is required.");
        return CustomerBasketOperationHash.Payload(new
        {
            Scope = "CustomerOrderCancellationOwner",
            User = userId,
            Session = userId.HasValue ? Guid.Empty : session
        });
    }

    public static void Validate(CustomerOrderCancellationIntentDto intent)
    {
        if (intent.SchemaVersion != 1 || intent.OperationId == Guid.Empty)
            throw new BadRequestException("A supported original cancellation intent is required.");
        if (!string.Equals(intent.PayloadHash, Payload(intent.OrderId, intent.ExpectedVersion), StringComparison.Ordinal))
            throw new ConflictException("The cancellation details changed. Check the original attempt.");
    }
}
