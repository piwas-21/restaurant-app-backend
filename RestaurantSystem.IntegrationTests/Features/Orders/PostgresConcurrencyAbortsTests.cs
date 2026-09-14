using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class PostgresConcurrencyAbortsTests
{
    [Theory]
    [InlineData(PostgresConcurrencyAborts.SerializationFailure)]
    [InlineData(PostgresConcurrencyAborts.Deadlock)]
    [InlineData(PostgresConcurrencyAborts.AbortedTransaction)]
    public void Direct_abort_is_recognized(string sqlState)
    {
        var exception = new PostgresException("concurrency abort", "40P01", "XX", sqlState);

        PostgresConcurrencyAborts.IsMatch(exception, out var actual).Should().BeTrue();
        actual.Should().Be(sqlState);
    }

    [Fact]
    public void Ef_wrapped_abort_is_recognized()
    {
        var postgres = new PostgresException(
            "could not serialize access", "40001", "XX40001", PostgresConcurrencyAborts.SerializationFailure);

        PostgresConcurrencyAborts.IsMatch(new DbUpdateException("save failed", postgres), out var actual)
            .Should().BeTrue();
        actual.Should().Be(PostgresConcurrencyAborts.SerializationFailure);
    }

    [Fact]
    public void Ordinary_database_error_is_not_mapped_as_a_concurrency_abort()
    {
        PostgresConcurrencyAborts.IsMatch(new DbUpdateException("not a race"), out var actual)
            .Should().BeFalse();
        actual.Should().BeEmpty();
    }
}
