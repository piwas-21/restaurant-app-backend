using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Queries.GetCustomerLoyaltyBalanceQuery;
using RestaurantSystem.Api.Features.FidelityPoints.Queries.GetCustomerLoyaltyHistoryQuery;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

public sealed partial class CustomerNativeLoyaltyReadTests
{
    [Fact]
    public async Task History_scopes_before_SQL_limit_and_projects_no_entity_or_staff_graph()
    {
        var foreign = Enumerable.Range(501, 100).Select(index =>
        {
            var row = Movement(index, Foreign);
            row.CreatedAt = Day.AddDays(1);
            return row;
        });
        await SeedAsync(Enumerable.Range(1, 500).Reverse().Select(index => Movement(index, Owner)).Concat(foreign));
        var capture = new LoyaltyReadCapture();
        await using var context = DatabaseFixture.CreateContext(capture);
        Assert.Equal(600, await context.FidelityPointsTransactions.CountAsync());
        Assert.Single(capture.Commands);
        capture.Commands.Clear();
        using var scope = Factory.Services.CreateScope();
        var page = await new GetCustomerLoyaltyHistoryQueryHandler(context,
            scope.ServiceProvider.GetRequiredService<CustomMediator>(), Actor().Object).Handle(new(1), default);
        Assert.NotNull(page.Data);
        Assert.Equal(Enumerable.Range(1, 20).Select(Id), page.Data.Items.Select(row => row.TransactionId));
        var command = Assert.Single(capture.Commands, command => command.Sql.Contains("FROM fidelity_points_transactions", StringComparison.Ordinal));
        Assert.Contains("user_id = @", command.Sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT @", command.Sql, StringComparison.Ordinal);
        Assert.Contains(21, command.Parameters);
        foreach (var forbidden in new[] { "description", "order_id", "order_total", "created_by", "original_transaction_id", "JOIN orders" })
            Assert.DoesNotContain(forbidden, command.Sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false, "role")]
    [InlineData(true, "role")]
    [InlineData(false, "account")]
    [InlineData(true, "account")]
    [InlineData(false, "capability")]
    [InlineData(true, "capability")]
    [InlineData(false, "tenant")]
    [InlineData(true, "tenant")]
    [InlineData(false, "currency")]
    [InlineData(true, "currency")]
    public async Task Withdrawal_after_materialization_prevents_private_results(bool balance, string withdrawal)
    {
        await SeedAsync([Movement(1, Owner)]);
        using var scope = Factory.Services.CreateScope();
        var mobile = scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        var capture = new LoyaltyReadCapture();
        var observed = false;
        capture.AfterLoyaltyRead = async () =>
        {
            observed = true;
            if (withdrawal == "capability") mobile.EnabledCapabilities = [];
            else if (withdrawal == "tenant") mobile.TenantId = Guid.NewGuid();
            else
            {
                await using var observer = DatabaseFixture.CreateContext();
                if (withdrawal == "currency")
                {
                    var info = await observer.RestaurantInfo.SingleAsync();
                    info.Currency = info.Currency == "JPY" ? "CHF" : "JPY";
                }
                else
                {
                    var user = await observer.Users.SingleAsync(row => row.Id == Owner);
                    if (withdrawal == "role") user.Role = UserRole.Admin; else user.IsDeleted = true;
                }
                await observer.SaveChangesAsync();
            }
        };
        await using var context = DatabaseFixture.CreateContext(capture);
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        if (balance)
            await Assert.ThrowsAsync<NotFoundException>(() => new GetCustomerLoyaltyBalanceQueryHandler(context, mediator,
                Actor().Object).Handle(new(1), default));
        else
            await Assert.ThrowsAsync<NotFoundException>(() => new GetCustomerLoyaltyHistoryQueryHandler(context, mediator,
                Actor().Object).Handle(new(1), default));
        Assert.True(observed);
    }

    [Theory]
    [InlineData(TransactionType.Earned, "earned")]
    [InlineData(TransactionType.Redeemed, "redeemed")]
    [InlineData(TransactionType.AdminAdjustment, "adjusted")]
    [InlineData(TransactionType.Expired, "expired")]
    [InlineData(TransactionType.Refunded, "refunded")]
    [InlineData(TransactionType.EarnedClawback, "earned-reversed")]
    [InlineData(TransactionType.RedemptionRestored, "redemption-restored")]
    public async Task All_current_ledger_kinds_have_stable_customer_labels(TransactionType kind, string expected)
    {
        Authenticate();
        var movement = Movement(1, Owner);
        movement.TransactionType = kind;
        await SeedAsync([movement]);
        var page = await ReadAsync("/history?schemaVersion=1");
        Assert.Equal(expected, page.GetProperty("items")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Unknown_ledger_kind_refuses_instead_of_exposing_an_internal_enum_value()
    {
        Authenticate();
        var movement = Movement(1, Owner);
        movement.TransactionType = (TransactionType)999;
        await SeedAsync([movement]);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.GetAsync(Route + "/history?schemaVersion=1")).StatusCode);
    }

    [Fact]
    public async Task Machine_credentials_cannot_substitute_for_a_customer()
    {
        using var scope = Factory.Services.CreateScope();
        await using var context = DatabaseFixture.CreateContext();
        var actor = Actor();
        actor.SetupGet(value => value.IsApiToken).Returns(true);
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        await Assert.ThrowsAsync<ForbiddenException>(() => new GetCustomerLoyaltyBalanceQueryHandler(context, mediator,
            actor.Object).Handle(new(1), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => new GetCustomerLoyaltyHistoryQueryHandler(context, mediator,
            actor.Object).Handle(new(1), default));
    }

    private static Mock<ICurrentUserService> Actor()
    {
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(value => value.IsAuthenticated).Returns(true);
        actor.SetupGet(value => value.UserId).Returns(Owner);
        actor.SetupGet(value => value.Role).Returns(UserRole.Customer);
        return actor;
    }
    private sealed class LoyaltyReadCapture : DbCommandInterceptor
    {
        public List<(string Sql, object?[] Parameters)> Commands { get; } = [];
        public Func<Task>? AfterLoyaltyRead { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray()));
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if ((command.CommandText.Contains("FROM fidelity_points_transactions", StringComparison.Ordinal)
                || command.CommandText.Contains("FROM fidelity_point_balances", StringComparison.Ordinal))
                && AfterLoyaltyRead is { } action)
            {
                AfterLoyaltyRead = null;
                await action();
            }
            return result;
        }
    }
}
