using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Seeders;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// Issue #120 (saas-prep): <see cref="RestaurantInfoSeeder"/> replaces the
/// migration-seeded tenant-1 (RUMI) identity with RestaurantInfoSeed values
/// while the singleton is pristine (UpdatedAt == null), and never touches it
/// once modified or when the section is unset. The RestaurantInfo tables are
/// Respawn-ignored, so every test restores the migration state in a finally
/// block (via ExecuteUpdate, which bypasses the audit interceptor that would
/// otherwise re-stamp UpdatedAt).
/// </summary>
[Collection("Database Lane 3")]
public class RestaurantInfoSeederTests
{
    private readonly DatabaseFixture _fixture;

    public RestaurantInfoSeederTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static RestaurantInfoSeedSettings TenantSeed() => new()
    {
        Name = "Demo Restaurant",
        City = "Amsterdam",
        Email = "admin@demo.test",
    };

    [Fact]
    public async Task PristineRow_SeedConfigured_ReplacesMigrationDefaultsAndSeededPhone()
    {
        var original = await SnapshotAsync();
        try
        {
            await MakePristineAsync();

            await using var context = _fixture.CreateContext();
            await RestaurantInfoSeeder.SeedAsync(context, NullLogger.Instance, TenantSeed());

            await using var verify = _fixture.CreateContext();
            var seeded = await verify.RestaurantInfo.Include(r => r.PhoneNumbers).SingleAsync();
            seeded.Name.Should().Be("Demo Restaurant");
            seeded.City.Should().Be("Amsterdam");
            seeded.Email.Should().Be("admin@demo.test");
            // Tenant-1 identity the registry doesn't cover must not leak into
            // a fresh tenant install: address blanked, seeded phone removed.
            seeded.AddressLine1.Should().BeEmpty();
            seeded.PostalCode.Should().BeEmpty();
            seeded.Country.Should().BeEmpty();
            seeded.Website.Should().BeNull();
            seeded.PhoneNumbers.Should().BeEmpty();
            // The seeder's own save stamps UpdatedAt → a second boot skips.
            seeded.UpdatedAt.Should().NotBeNull();
        }
        finally
        {
            await RestoreAsync(original);
        }
    }

    [Fact]
    public async Task ModifiedRow_SeedConfigured_LeavesSingletonUntouched()
    {
        var original = await SnapshotAsync();
        try
        {
            // Simulate an admin-owned row: any modification stamps UpdatedAt.
            await using (var stamp = _fixture.CreateContext())
            {
                await stamp.RestaurantInfo.ExecuteUpdateAsync(s =>
                    s.SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
            }

            await using var context = _fixture.CreateContext();
            await RestaurantInfoSeeder.SeedAsync(context, NullLogger.Instance, TenantSeed());

            await using var verify = _fixture.CreateContext();
            var after = await verify.RestaurantInfo.Include(r => r.PhoneNumbers).SingleAsync();
            after.Name.Should().Be(original.Name);
            after.Email.Should().Be(original.Email);
            after.AddressLine1.Should().Be(original.AddressLine1);
            after.PhoneNumbers.Should().HaveCount(original.PhoneNumbers.Count);
        }
        finally
        {
            await RestoreAsync(original);
        }
    }

    [Fact]
    public async Task PristineRow_SeedNotConfigured_LeavesMigrationDefaults()
    {
        var original = await SnapshotAsync();
        try
        {
            await MakePristineAsync();

            await using var context = _fixture.CreateContext();
            await RestaurantInfoSeeder.SeedAsync(context, NullLogger.Instance, new RestaurantInfoSeedSettings());

            await using var verify = _fixture.CreateContext();
            var after = await verify.RestaurantInfo.Include(r => r.PhoneNumbers).SingleAsync();
            after.Name.Should().Be(original.Name);
            after.UpdatedAt.Should().BeNull();
            after.PhoneNumbers.Should().HaveCount(original.PhoneNumbers.Count);
        }
        finally
        {
            await RestoreAsync(original);
        }
    }

    private async Task<RestaurantInfo> SnapshotAsync()
    {
        await using var context = _fixture.CreateContext();
        return await context.RestaurantInfo.AsNoTracking().Include(r => r.PhoneNumbers).SingleAsync();
    }

    /// <summary>
    /// Nulls the audit stamp so the row looks migration-fresh regardless of
    /// what earlier suite tests did to it. ExecuteUpdate bypasses the audit
    /// interceptor, which would otherwise immediately re-stamp UpdatedAt.
    /// </summary>
    private async Task MakePristineAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.RestaurantInfo.ExecuteUpdateAsync(s => s
            .SetProperty(r => r.UpdatedAt, (DateTime?)null)
            .SetProperty(r => r.UpdatedBy, (string?)null));
    }

    /// <summary>
    /// Writes the captured state back (values, audit stamp, phone rows) so the
    /// Respawn-ignored singleton looks untouched to the rest of the suite.
    /// </summary>
    private async Task RestoreAsync(RestaurantInfo original)
    {
        await using var context = _fixture.CreateContext();
        await context.RestaurantInfo.ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Name, original.Name)
            .SetProperty(r => r.AddressLine1, original.AddressLine1)
            .SetProperty(r => r.AddressLine2, original.AddressLine2)
            .SetProperty(r => r.City, original.City)
            .SetProperty(r => r.PostalCode, original.PostalCode)
            .SetProperty(r => r.Country, original.Country)
            .SetProperty(r => r.Latitude, original.Latitude)
            .SetProperty(r => r.Longitude, original.Longitude)
            .SetProperty(r => r.Email, original.Email)
            .SetProperty(r => r.Website, original.Website)
            .SetProperty(r => r.UpdatedAt, original.UpdatedAt)
            .SetProperty(r => r.UpdatedBy, original.UpdatedBy));

        await context.RestaurantPhoneNumbers.ExecuteDeleteAsync();
        foreach (var phone in original.PhoneNumbers)
        {
            context.RestaurantPhoneNumbers.Add(new RestaurantPhoneNumber
            {
                Id = phone.Id,
                RestaurantInfoId = phone.RestaurantInfoId,
                Label = phone.Label,
                Number = phone.Number,
                WhatsAppEnabled = phone.WhatsAppEnabled,
                DisplayOrder = phone.DisplayOrder,
                IsActive = phone.IsActive,
                CreatedAt = phone.CreatedAt,
                CreatedBy = phone.CreatedBy,
            });
        }
        await context.SaveChangesAsync();
    }
}
