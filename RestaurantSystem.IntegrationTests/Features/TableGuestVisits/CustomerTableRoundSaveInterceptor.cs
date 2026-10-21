using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

internal sealed class CustomerTableRoundSaveInterceptor : SaveChangesInterceptor
{
    internal Func<ApplicationDbContext, Task>? Before { get; set; }
    internal Func<ApplicationDbContext, Task>? After { get; set; }
    internal bool Triggered { get; private set; }
    private bool pending;
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!Triggered && eventData.Context is ApplicationDbContext context
            && context.ChangeTracker.Entries<CustomerOrderOperation>().Any(entry => entry.State == EntityState.Added))
        {
            Triggered = true; pending = true;
            if (Before is { } before) await before(context);
        }
        return result;
    }
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (pending && eventData.Context is ApplicationDbContext context)
        {
            pending = false;
            if (After is { } after) await after(context);
        }
        return result;
    }
}
