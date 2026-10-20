using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class CustomerOrderHistoryTests
{
    [Fact]
    public async Task History_receipt_bounds_frozen_line_and_ingredient_queries_without_tracking_or_catalogue_joins()
    {
        await SeedHistoryDetailAsync(false);
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(value => value.UserId).Returns(Owner);
        actor.SetupGet(value => value.IsAuthenticated).Returns(true);
        actor.SetupGet(value => value.Role).Returns(UserRole.Customer);
        var clock = new Mock<ITenantClock>();
        clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.Utc);
        var reads = new ReceiptReadCapture();
        await using var context = DatabaseFixture.CreateContext(reads);
        (await context.Orders.CountAsync()).Should().Be(1);
        reads.Queries.Should().ContainSingle("a known read proves the interceptor is attached");
        reads.Queries.Clear();
        var reader = new CustomerOrderReceiptReader(context, Mock.Of<ICustomerCheckoutGuard>(),
            Mock.Of<ICustomerOrderOperationStore>(), clock.Object, actor.Object);
        (await reader.ReadCustomerAsync(Id(1), CancellationToken.None)).Items.Should().ContainSingle();
        reads.Queries.Should().HaveCount(5);
        var lines = reads.Queries.Single(query => query.Sql.Contains("FROM \"OrderItems\"", StringComparison.Ordinal));
        lines.Sql.Should().Contain("LIMIT @");
        lines.Values.Should().Contain(1001);
        var ingredients = reads.Queries.Single(query => query.Sql.Contains("FROM \"OrderItemIngredients\"", StringComparison.Ordinal));
        ingredients.Sql.Should().Contain("LIMIT @");
        ingredients.Values.Should().Contain(10001);
        reads.Queries.Should().OnlyContain(query => !query.Sql.Contains("JOIN \"Products\"", StringComparison.Ordinal));
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    private sealed class ReceiptReadCapture : DbCommandInterceptor
    {
        public List<(string Sql, object?[] Values)> Queries { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Queries.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(value => value.Value).ToArray()));
            return ValueTask.FromResult(result);
        }
    }
}
