using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class CustomerOrderOperationAuthorizationTests(DatabaseFixture database) : CustomerOrderOperationFixture(database)
{
    [Fact]
    public async Task Another_guest_session_cannot_read_cancel_or_execute_an_existing_operation()
    {
        await using var context = Database.CreateContext();
        var store = Store(context);
        await store.ExecuteAsync(Session, Operation, Payload, () => SaveOrderAsync(context), default);
        var other = Guid.NewGuid().ToString("D");
        var recover = () => store.RecoverAsync(other, Operation, Payload, default);
        var cancel = () => store.CancelAsync(other, Operation, Payload, default);
        var execute = () => store.ExecuteAsync(other, Operation, Payload, () => SaveOrderAsync(context), default);
        await recover.Should().ThrowAsync<NotFoundException>();
        await cancel.Should().ThrowAsync<NotFoundException>();
        await execute.Should().ThrowAsync<NotFoundException>();
        (await context.Orders.CountAsync()).Should().Be(1);
    }
    [Fact]
    public async Task Another_customer_on_the_same_device_session_cannot_read_a_guest_operation()
    {
        await using var context = Database.CreateContext();
        await Store(context).CancelAsync(Session, Operation, Payload, default);
        var customer = Store(context, Guid.NewGuid(), authenticated: true, role: UserRole.Customer);
        var recover = () => customer.RecoverAsync(Session, Operation, Payload, default);
        await recover.Should().ThrowAsync<NotFoundException>();
    }
    [Fact]
    public async Task A_reused_operation_with_different_order_details_is_refused_on_every_route()
    {
        await using var context = Database.CreateContext();
        var store = Store(context);
        await store.CancelAsync(Session, Operation, Payload, default);
        var other = new string('b', 64);
        var recover = () => store.RecoverAsync(Session, Operation, other, default);
        var cancel = () => store.CancelAsync(Session, Operation, other, default);
        var execute = () => store.ExecuteAsync(Session, Operation, other, () => SaveOrderAsync(context), default);
        await recover.Should().ThrowAsync<ConflictException>();
        await cancel.Should().ThrowAsync<ConflictException>();
        await execute.Should().ThrowAsync<ConflictException>();
        (await context.Orders.CountAsync()).Should().Be(0);
    }
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task Staff_are_refused_even_for_an_unseen_operation(UserRole role)
    {
        await using var context = Database.CreateContext();
        var recover = () => Store(context, Guid.NewGuid(), true, role).RecoverAsync(Session, Operation, Payload, default);
        await recover.Should().ThrowAsync<ForbiddenException>();
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
    }
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Machine_rejected_bearer_and_authenticated_missing_owner_are_refused(bool machine, bool rejected, bool missingOwner)
    {
        await using var context = Database.CreateContext();
        var recover = () => Store(context, authenticated: missingOwner, role: UserRole.Customer,
            machine: machine, rejectedAuthorization: rejected).RecoverAsync(Session, Operation, Payload, default);
        if (machine) await recover.Should().ThrowAsync<ForbiddenException>();
        else await recover.Should().ThrowAsync<UnauthorizedAccessException>();
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
    }
}
