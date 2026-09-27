using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

/// <summary>
/// Persist the worst-case charge before making a provider call. A timeout or process failure
/// leaves the reservation charged, so retries cannot evade the tenant's daily limit.
/// </summary>
internal static class TranslationGenerationReservation
{
    public static async Task SaveAsync(
        IServiceScopeFactory scopeFactory,
        TranslationGenerationBatch batch,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.TranslationGenerationBatches.Add(batch);
        await context.SaveChangesAsync(cancellationToken);
    }
}
