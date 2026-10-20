using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationCancellationAccess(ApplicationDbContext context, ICurrentUserService current,
    IOptions<CustomerMobileSettings> settings, CustomMediator mediator, ITenantClock clock)
    : ICustomerReservationCancellationAccess
{
    public string AuditIdentifier => current.GetAuditIdentifier();
    public CustomerReservationOperationOwner Capture(string sessionId, int? version, Guid operationId, Guid tenantId)
    {
        var customer = CustomerAccountAccess.RequireOwner(current);
        var session = CustomerBasketOperationHash.Session(sessionId);
        if (version != 1 || operationId == Guid.Empty)
            throw new BadRequestException("A supported booking schema and operation ID are required.");
        if (tenantId == Guid.Empty || tenantId != settings.Value.TenantId)
            throw new NotFoundException("Customer reservation operation unavailable.");
        return new(customer, CustomerBasketOperationHash.Owner(session, customer), string.Empty);
    }
    private static Guid Customer(CustomerReservationOperationOwner owner) => owner.CustomerId
        ?? throw new InvalidOperationException("Cancellation requires a captured customer.");

    public Task LockCustomerAsync(CustomerReservationOperationOwner owner, CancellationToken cancellationToken) =>
        CustomerAccountWriteGuard.AcquireAsync(context, Customer(owner), true, cancellationToken);

    public async Task<Reservation> LockBookingAsync(Guid reservationId, CustomerReservationOperationOwner owner,
        CancellationToken cancellationToken)
    {
        // Existing booking precedes the customer row: erasure scrubs bookings before deleting Users.
        var found = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM "Reservations"
            WHERE id = {reservationId} AND customer_id = {owner.CustomerId} FOR UPDATE
            """).ToArrayAsync(cancellationToken);
        if (found.Length != 1) throw new NotFoundException("Customer reservation unavailable.");
        // No tracking: a previously loaded entity must not hide a staff edit while we waited.
        return await CustomerReservationReadAccess.Scope(context, Customer(owner))
            .SingleOrDefaultAsync(row => row.Id == reservationId, cancellationToken)
            ?? throw new NotFoundException("Customer reservation unavailable.");
    }

    public async Task RequireAcceptingAsync(Guid tenantId, string timeZone, CancellationToken cancellationToken)
    {
        var bootstrap = await CustomerReservationReadAccess.RequireAvailableAsync(mediator, cancellationToken);
        if (bootstrap.TenantId != tenantId || bootstrap.Timezone != timeZone
            || bootstrap.Capabilities.GetValueOrDefault("accounts") != "enabled")
            throw new NotFoundException("Customer reservations are unavailable. Refresh this restaurant.");
    }
    public void EnsureEditable(Reservation reservation) => CustomerReservationEditability.RequireFuture(
        reservation.Status, DateOnly.FromDateTime(reservation.ReservationDate), reservation.StartTime, clock.Now);
}
