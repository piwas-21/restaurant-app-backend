using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Commands.SettleCheckoutSessionCommand;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutWebExclusionTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    [Fact]
    public async Task Both_ordinary_terminal_read_and_direct_writer_refuse_native_money_without_original_proof()
    {
        await SeedAsync();
        await WithCheckoutAsync(async (_, _, leases) =>
        {
            var lease = await leases.AcquireAsync(Session, Proof, CancellationToken.None);
            await leases.RecordAsync(Session, Proof, lease, CustomerOrderCheckoutSettlementTests.Paid(lease), CancellationToken.None);
            return true;
        });
        await using var context = Database.CreateContext();
        var row = await context.OrderCheckoutSessions.AsNoTracking().SingleAsync();
        var provider = new Mock<IStripeCheckoutClient>(MockBehavior.Strict);
        var writer = new CheckoutSettlementWriter(context, Mock.Of<IOrderPaymentBuilder>(), Mock.Of<IOrderFidelityCoordinator>(),
            Mock.Of<ISettlementNotifier>(), Mock.Of<ICurrentUserService>(), NullLogger<CheckoutSettlementWriter>.Instance,
            Mock.Of<IOrderPreparationService>());
        var handler = new SettleCheckoutSessionCommandHandler(context, provider.Object, writer,
            Mock.Of<ICheckoutSessionRetirement>(), NullLogger<SettleCheckoutSessionCommandHandler>.Instance);
        await FluentActions.Invoking(() => handler.Handle(new() { SessionId = row.SessionId }, CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
        await FluentActions.Invoking(() => writer.SettleAsync(row, row.PaymentIntentId, row.AmountMinor, CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
        provider.VerifyNoOtherCalls();
        (await context.OrderPayments.CountAsync()).Should().Be(1);
        (await context.Orders.SingleAsync()).TotalPaid.Should().Be(12.34m);
    }
}
