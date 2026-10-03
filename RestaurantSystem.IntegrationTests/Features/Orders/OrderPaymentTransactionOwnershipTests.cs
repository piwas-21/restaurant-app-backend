using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.AddPaymentToOrderCommand;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class OrderPaymentTransactionOwnershipTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Ambient_transaction_is_refused_before_any_tender_work()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Database.CurrentTransaction.Should().NotBeNull();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(value => value.Role).Returns(UserRole.Cashier);
        var applicator = new Mock<IOrderPaymentApplicator>(MockBehavior.Strict);
        var mapping = new Mock<IOrderMappingService>(MockBehavior.Strict);
        var handler = new AddPaymentToOrderCommandHandler(context, applicator.Object, mapping.Object, user.Object);

        var act = () => handler.Handle(new AddPaymentToOrderCommand
        {
            OrderId = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 10m
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        applicator.VerifyNoOtherCalls();
        mapping.VerifyNoOtherCalls();
        await transaction.CommitAsync();
        (await context.OrderPayments.CountAsync()).Should().Be(0);
    }
}
