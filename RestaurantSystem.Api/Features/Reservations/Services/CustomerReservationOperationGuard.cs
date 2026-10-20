using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationOperationGuard(ApplicationDbContext context, ICurrentUserService currentUser,
    IHttpContextAccessor httpContext, IOptions<CustomerMobileSettings> settings, CustomMediator mediator)
    : ICustomerReservationOperationGuard
{
    public CustomerReservationOperationOwner Capture(string sessionId, CustomerReservationCreateDto request)
    {
        CustomerBasketAccess.EnsureCustomerOrGuest(currentUser, httpContext.HttpContext);
        var session = CustomerBasketOperationHash.Session(sessionId);
        Guid? customer = currentUser.IsAuthenticated
            ? currentUser.UserId ?? throw new UnauthorizedAccessException("Renew customer authentication before booking.")
            : null;
        if (request.SchemaVersion != 1 || request.OperationId == Guid.Empty)
            throw new BadRequestException("A supported booking schema and operation ID are required.");
        // Recovery remains possible after capability/module withdrawal, but never across tenant identity.
        if (request.TenantId == Guid.Empty || request.TenantId != settings.Value.TenantId)
            throw new NotFoundException("Customer reservation operation unavailable.");
        return new(customer, CustomerBasketOperationHash.Owner(session, customer),
            CustomerBasketOperationHash.Payload(new { Purpose = "customer-reservation-create-v1", Request = request }));
    }

    public Task LockOwnerAsync(CustomerReservationOperationOwner owner, CancellationToken cancellationToken) =>
        owner.CustomerId is { } customer
            ? CustomerAccountWriteGuard.AcquireAsync(context, customer, true, cancellationToken)
            : Task.CompletedTask;

    public async Task RequireAcceptingAsync(CustomerReservationCreateDto request, CustomerReservationOperationOwner owner,
        CancellationToken cancellationToken)
    {
        var bootstrap = await CustomerReservationReadAccess.RequireAvailableAsync(mediator, cancellationToken);
        if (request.TenantId != bootstrap.TenantId || request.TimeZone != bootstrap.Timezone
            || owner.CustomerId.HasValue && bootstrap.Capabilities.GetValueOrDefault("accounts") != "enabled")
            throw new NotFoundException("Customer reservations are unavailable. Refresh this restaurant before booking.");
    }
}
