using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.Products.Services;

namespace RestaurantSystem.IntegrationTests.Features.Products;

public sealed class CustomerStepManifestConcurrencyConflictTests
{
    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    public void SerializablePostgresWriteRacesAreManifestRevisionConflicts(string sqlState)
    {
        var providerError = new PostgresException("concurrent write lost", "ERROR", "ERROR", sqlState);
        var efError = new DbUpdateException("The manifest write failed.", providerError);

        CustomerStepManifestStore.IsRevisionWriteConflict(efError).Should().BeTrue();
    }

    [Fact]
    public void UnrelatedDatabaseFailuresDoNotBecomeManifestRevisionConflicts()
    {
        var providerError = new PostgresException("duplicate key", "ERROR", "ERROR", "23505");

        CustomerStepManifestStore.IsRevisionWriteConflict(providerError).Should().BeFalse();
    }
}
