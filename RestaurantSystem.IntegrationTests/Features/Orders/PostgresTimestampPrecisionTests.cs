using FluentAssertions;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class PostgresTimestampPrecisionTests
{
    [Fact]
    public void Matches_json_timestamp_truncated_by_postgresql_but_rejects_one_microsecond_difference()
    {
        var serialized = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var persisted = serialized.AddTicks(-7);

        PostgresTimestampPrecision.MatchesColumn(serialized, persisted).Should().BeTrue();
        PostgresTimestampPrecision.MatchesColumn(serialized, persisted.AddTicks(10)).Should().BeFalse(
            "one microsecond is representable and must remain an integrity mismatch");
        PostgresTimestampPrecision.MatchesColumn(serialized, persisted.AddTicks(1)).Should().BeFalse(
            "a persisted value with precision PostgreSQL cannot store is invalid");
        PostgresTimestampPrecision.MatchesColumn(null, null).Should().BeTrue();
        PostgresTimestampPrecision.MatchesColumn(serialized, null).Should().BeFalse();
        PostgresTimestampPrecision.MatchesColumn(null, persisted).Should().BeFalse(
            "a missing JSON timestamp cannot prove a populated database value");
    }
}
