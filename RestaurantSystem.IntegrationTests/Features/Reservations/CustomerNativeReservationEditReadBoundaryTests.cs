using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Queries.GetCustomerReservationEditAvailabilityQuery;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditReadTests
{
    [Fact]
    public async Task Previously_tracked_contacts_and_table_capacities_cannot_hide_committed_editor_changes()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        await db.Tables.ToListAsync(); await db.Reservations.Include(row => row.CombinedTables).ToListAsync();
        await using (var observer = DatabaseFixture.CreateContext())
        {
            await observer.Reservations.Where(row => row.Id == _booking).ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerPhone, "Fresh phone"));
            await observer.Tables.Where(row => row.Id == _first).ExecuteUpdateAsync(update => update.SetProperty(row => row.MaxGuests, 1));
        }
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var access = new CustomerReservationEditReadAccess(db, mediator, Actor().Object, _clock.Object);
        Assert.Equal("Fresh phone", (await access.ReadAsync(_booking, default)).Context.Contact.CustomerPhone);
        var value = await new GetCustomerReservationEditAvailabilityQueryHandler(access, mediator, _clock.Object)
            .Handle(new(1, _booking, DateOnly.FromDateTime(Day), 6, "Europe/Zurich"), default);
        Assert.NotNull(value.Data); Assert.Empty(value.Data.Slots);
        Assert.False(db.ChangeTracker.HasChanges());
    }
    [Theory]
    [InlineData("account")]
    [InlineData("role")]
    [InlineData("revision")]
    public async Task Withdrawal_after_the_actual_private_projection_cannot_return_editor_contacts(string mode)
    {
        using var scope = Factory.Services.CreateScope();
        var interceptor = new ProjectionBoundary(); var observed = false;
        interceptor.AfterRead = async () =>
        {
            observed = true; await using var observer = DatabaseFixture.CreateContext();
            if (mode == "account") await observer.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            else if (mode == "role") await observer.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, UserRole.Admin));
            else await observer.Reservations.Where(row => row.Id == _booking).ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerName, "Changed"));
        };
        await using var db = DatabaseFixture.CreateContext(interceptor);
        var access = new CustomerReservationEditReadAccess(db, scope.ServiceProvider.GetRequiredService<CustomMediator>(), Actor().Object, _clock.Object);
        if (mode == "revision") await Assert.ThrowsAsync<ConflictException>(() => access.ReadAsync(_booking, default));
        else await Assert.ThrowsAsync<NotFoundException>(() => access.ReadAsync(_booking, default));
        Assert.True(observed); Assert.Empty(db.ChangeTracker.Entries());
        Assert.NotNull(interceptor.Projection);
        Assert.Contains("customer_email", interceptor.Projection, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "notes", "created_by", "JOIN tables", "qr_code" })
            Assert.DoesNotContain(forbidden, interceptor.Projection, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Machine_authority_cannot_substitute_for_the_customer_editor()
    {
        using var scope = Factory.Services.CreateScope(); await using var db = DatabaseFixture.CreateContext();
        var actor = Actor(); actor.SetupGet(value => value.IsApiToken).Returns(true);
        var access = new CustomerReservationEditReadAccess(db, scope.ServiceProvider.GetRequiredService<CustomMediator>(), actor.Object, _clock.Object);
        await Assert.ThrowsAsync<ForbiddenException>(() => access.ReadAsync(_booking, default));
        Assert.Empty(db.ChangeTracker.Entries());
    }
    private static Mock<ICurrentUserService> Actor()
    {
        var actor = new Mock<ICurrentUserService>(); actor.SetupGet(value => value.IsAuthenticated).Returns(true);
        actor.SetupGet(value => value.UserId).Returns(Owner); actor.SetupGet(value => value.Role).Returns(UserRole.Customer);
        return actor;
    }
    private sealed class ProjectionBoundary : DbCommandInterceptor
    {
        public Func<Task>? AfterRead { get; set; }
        public string? Projection { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Reservations\"", StringComparison.Ordinal) && AfterRead is { } action)
            { AfterRead = null; Projection = command.CommandText; await action(); }
            return result;
        }
    }
}
