using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationQuery;
using RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationsQuery;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationReadTests
{
    [Fact]
    public async Task History_bounds_the_owned_projection_in_SQL_and_leaves_no_tracked_graph()
    {
        // Foreign rows sort before owned ones. Applying ownership after LIMIT loses the whole page.
        var foreign = Enumerable.Range(501, 100).Select(index =>
        {
            var row = Reservation(index, Foreign);
            row.ReservationDate = Day.AddDays(1);
            return row;
        });
        await SeedAsync(Enumerable.Range(1, 500).Reverse().Select(index => Reservation(index, Owner)).Concat(foreign));
        var capture = new ReservationReadCapture();
        await using var context = DatabaseFixture.CreateContext(capture);
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET TIME ZONE 'America/Los_Angeles'");
        Assert.Equal(600, await context.Reservations.CountAsync());
        Assert.Single(capture.Commands); // positive control for the SQL instrument
        capture.Commands.Clear();
        using var scope = Factory.Services.CreateScope();
        var page = await new GetCustomerReservationsQueryHandler(context,
            scope.ServiceProvider.GetRequiredService<CustomMediator>(), Actor().Object,
            scope.ServiceProvider.GetRequiredService<ITenantClock>()).Handle(new(1), default);
        Assert.NotNull(page.Data);
        Assert.All(page.Data.Items, row => Assert.Equal(new DateOnly(2026, 11, 1), row.ReservationDate));
        Assert.Equal(Enumerable.Range(1, 20).Select(Id), page.Data.Items.Select(row => row.ReservationId));
        var command = Assert.Single(capture.Commands, command => command.Sql.Contains("FROM \"Reservations\"", StringComparison.Ordinal));
        Assert.Contains("customer_id = @", command.Sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT @", command.Sql, StringComparison.Ordinal);
        Assert.Contains(21, command.Parameters);
        Assert.DoesNotContain("customer_email", command.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("notes", command.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN tables", command.Sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false, "role")]
    [InlineData(true, "role")]
    [InlineData(false, "account")]
    [InlineData(true, "account")]
    [InlineData(false, "capability")]
    [InlineData(true, "capability")]
    public async Task Withdrawal_after_the_reservation_query_prevents_a_private_result(bool detail, string withdrawal)
    {
        await SeedAsync([Reservation(1, Owner)]);
        using var scope = Factory.Services.CreateScope();
        var mobile = scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        var capture = new ReservationReadCapture();
        var observed = false;
        capture.AfterReservationRead = async () =>
        {
            observed = true;
            if (withdrawal == "capability") mobile.EnabledCapabilities = [];
            else
            {
                await using var observer = DatabaseFixture.CreateContext();
                var user = await observer.Users.SingleAsync(row => row.Id == Owner);
                if (withdrawal == "role") user.Role = UserRole.Admin; else user.IsDeleted = true;
                await observer.SaveChangesAsync();
            }
        };
        await using var context = DatabaseFixture.CreateContext(capture);
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        if (detail)
            await Assert.ThrowsAsync<NotFoundException>(() => new GetCustomerReservationQueryHandler(context, mediator,
                Actor().Object).Handle(new(1, Id(1)), default));
        else
            await Assert.ThrowsAsync<NotFoundException>(() => new GetCustomerReservationsQueryHandler(context, mediator,
                Actor().Object, scope.ServiceProvider.GetRequiredService<ITenantClock>()).Handle(new(1), default));
        Assert.True(observed);
    }

    [Fact]
    public async Task Machine_credentials_cannot_substitute_for_a_customer()
    {
        using var scope = Factory.Services.CreateScope();
        await using var context = DatabaseFixture.CreateContext();
        var actor = Actor();
        actor.SetupGet(value => value.IsApiToken).Returns(true);
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        await Assert.ThrowsAsync<ForbiddenException>(() => new GetCustomerReservationQueryHandler(context, mediator,
            actor.Object).Handle(new(1, Id(1)), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => new GetCustomerReservationsQueryHandler(context, mediator,
            actor.Object, scope.ServiceProvider.GetRequiredService<ITenantClock>()).Handle(new(1), default));
    }

    private static Mock<ICurrentUserService> Actor()
    {
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(value => value.IsAuthenticated).Returns(true);
        actor.SetupGet(value => value.UserId).Returns(Owner);
        actor.SetupGet(value => value.Role).Returns(UserRole.Customer);
        return actor;
    }
    private sealed class ReservationReadCapture : DbCommandInterceptor
    {
        public List<(string Sql, object?[] Parameters)> Commands { get; } = [];
        public Func<Task>? AfterReservationRead { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray()));
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Reservations\"", StringComparison.Ordinal) && AfterReservationRead is { } action)
            {
                AfterReservationRead = null;
                await action();
            }
            return result;
        }
    }
}
