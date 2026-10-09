using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>Writes a #664-era snapshot under its published schema, omitting additive #146 metadata.</summary>
internal static class LegacyOrderBillingSnapshotFixture
{
    internal static Task InsertAsync(ApplicationDbContext context, OrderBillingSnapshot snapshot) =>
        LegacyEntityFixture.InsertAsync(context, snapshot, "earning_disposition");
}
