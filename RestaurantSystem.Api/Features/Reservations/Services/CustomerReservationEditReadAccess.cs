using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationEditReadAccess(ApplicationDbContext context, CustomMediator mediator,
    ICurrentUserService current, ITenantClock clock) : ICustomerReservationEditReadAccess
{
    public async Task<CustomerReservationEditReadSnapshot> ReadAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var owner = CustomerAccountAccess.RequireOwner(current);
        var bootstrap = await RequireBootstrapAsync(cancellationToken);
        var row = await CustomerReservationReadAccess.Scope(context, owner).Where(row => row.Id == reservationId)
            .Select(row => new ReadRow(new CustomerReservationReadRow(row.Id, row.ReservationDate, row.StartTime,
                    row.EndTime, row.NumberOfGuests, row.Status, row.SpecialRequests),
                new CustomerReservationContactDto(row.CustomerName, row.CustomerEmail, row.CustomerPhone),
                row.TableId, row.Revision)).SingleOrDefaultAsync(cancellationToken)
            ?? throw Unavailable();
        var tables = await context.Set<ReservationTable>().AsNoTracking().Where(row => row.ReservationId == reservationId)
            .Select(row => row.TableId).ToArrayAsync(cancellationToken);
        var result = new CustomerReservationEditReadSnapshot(owner, row.Revision, bootstrap.Currency!,
            new(1, bootstrap.TenantId, bootstrap.Timezone, CustomerReservationProjection.ToDto(row.Booking), row.Contact),
            tables.Append(row.TableId).Distinct().ToArray());
        await RevalidateAsync(result, cancellationToken);
        return result;
    }
    public async Task RevalidateAsync(CustomerReservationEditReadSnapshot original, CancellationToken cancellationToken)
    {
        if (CustomerAccountAccess.RequireOwner(current) != original.Owner) throw Unavailable();
        var fresh = await RequireBootstrapAsync(cancellationToken);
        if (fresh.TenantId != original.Context.TenantId || fresh.Timezone != original.Context.TimeZone
            || fresh.Currency != original.Currency) throw Unavailable();
        var revision = await CustomerReservationReadAccess.Scope(context, original.Owner)
            .Where(row => row.Id == original.Context.Reservation.ReservationId).Select(row => (Guid?)row.Revision)
            .SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();
        if (revision != original.Revision)
            throw new ConflictException("This booking changed. Refresh its current details.");
        var booking = original.Context.Reservation;
        CustomerReservationEditability.RequireFuture(booking.Status, booking.ReservationDate, booking.StartTime, clock.Now);
    }
    private async Task<CustomerMobileBootstrapDto> RequireBootstrapAsync(CancellationToken cancellationToken)
    {
        var bootstrap = await CustomerReservationReadAccess.RequireAvailableAsync(mediator, cancellationToken);
        if (bootstrap.Capabilities.GetValueOrDefault("accounts") != "enabled" || bootstrap.Currency is null)
            throw Unavailable();
        return bootstrap;
    }
    private static NotFoundException Unavailable() => new("Customer reservation editor unavailable.");
    private sealed record ReadRow(CustomerReservationReadRow Booking, CustomerReservationContactDto Contact, Guid TableId, Guid Revision);
}
