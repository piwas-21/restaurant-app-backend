using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence;

internal static class OrderBillingCreditInvariant
{
    internal static void AssertAppendOnly(ChangeTracker tracker)
    {
        if (tracker.Entries<OrderBillingCredit>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Billing credits are immutable; corrections require a new journal entry.");
    }
}
