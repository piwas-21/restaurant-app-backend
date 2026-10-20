using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public static class CustomerReservationReadAccess
{
    public static async Task<Guid> RequireEnabledAsync(CustomMediator mediator, ICurrentUserService current,
        CancellationToken cancellationToken)
    {
        var owner = CustomerAccountAccess.RequireOwner(current);
        var bootstrap = (await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken)).Data;
        if (bootstrap?.Capabilities.GetValueOrDefault("reservations") != "enabled")
            throw new NotFoundException("Customer reservations are unavailable.");
        return owner;
    }
    public static IQueryable<Reservation> Scope(ApplicationDbContext context, Guid owner)
    {
        var accounts = CustomerAccountAccess.ForOwner(context, owner);
        // Current database Customer ownership is inside the bounded query, not a later DTO filter.
        return context.Reservations.AsNoTracking().Where(row => row.CustomerId == owner
            && accounts.Any(user => user.Id == row.CustomerId));
    }
    public static async Task RequireCurrentAsync(ApplicationDbContext context, Guid owner, CancellationToken cancellationToken)
    {
        if (!await CustomerAccountAccess.ForOwner(context, owner).AsNoTracking().AnyAsync(cancellationToken))
            throw new NotFoundException("Customer account unavailable.");
    }
}
