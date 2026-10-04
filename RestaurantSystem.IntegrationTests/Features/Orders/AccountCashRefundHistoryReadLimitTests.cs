using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class AccountCashRefundHistoryReadLimitTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public async Task PostgreSQL_history_at_or_below_the_limit_is_returned_complete(int count)
    {
        await using var context = fixture.CreateContext();
        var query = context.Database.SqlQuery<int>($"SELECT generate_series(1, {count}) AS \"Value\"");

        var rows = await AccountCashRefundHistoryReadLimit.ReadAsync(query, CancellationToken.None);

        rows.Should().HaveCount(count);
        rows.Should().OnlyHaveUniqueItems();
        rows.Min().Should().Be(1);
        rows.Max().Should().Be(count);
    }

    [Fact]
    public async Task PostgreSQL_history_overflow_cannot_be_silently_truncated_into_valid_evidence()
    {
        await using var context = fixture.CreateContext();
        var count = 10001;
        var query = context.Database.SqlQuery<int>($"SELECT generate_series(1, {count}) AS \"Value\"");
        var read = () => AccountCashRefundHistoryReadLimit.ReadAsync(query, CancellationToken.None);

        await read.Should().ThrowAsync<ConflictException>().WithMessage("*reconciliation limit*");
    }
}
