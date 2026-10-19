using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Queries.GetCustomerOrderHistoryQuery;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class CustomerOrderHistoryTests
{
    [Fact]
    public async Task Large_history_uses_one_bounded_untracked_owner_projection()
    {
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Orders.AddRange(Enumerable.Range(1, 500).Reverse().Select(index => Order(index, Owner)));
            await seed.SaveChangesAsync();
        }
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(value => value.UserId).Returns(Owner);
        actor.SetupGet(value => value.IsAuthenticated).Returns(true);
        actor.SetupGet(value => value.Role).Returns(UserRole.Customer);
        var clock = new Mock<ITenantClock>();
        clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.Utc);
        var reads = new HistoryReadCapture();
        await using var context = DatabaseFixture.CreateContext(reads);
        (await context.Orders.CountAsync()).Should().Be(500);
        reads.Queries.Should().ContainSingle("a known read proves the interceptor is attached");
        reads.Queries.Clear();
        var handler = new GetCustomerOrderHistoryQueryHandler(context, actor.Object, clock.Object);
        var page = await handler.Handle(new(1), CancellationToken.None);
        page.Success.Should().BeTrue();
        page.Data!.Items.Select(item => item.OrderId).Should().Equal(Enumerable.Range(1, 20).Select(Id));
        page.Data.NextBefore!.OrderId.Should().Be(Id(20));
        var sql = reads.Queries.Should().ContainSingle().Which;
        sql.Should().Contain("row <= 21", "the order window must be bounded independently of the outer customer limit");
        sql.Should().Contain("user_id = @", "the history window must filter the owner before ranking");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    private sealed class HistoryReadCapture : DbCommandInterceptor
    {
        public List<string> Queries { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
