using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public partial class SettleCheckoutSessionCommandHandlerTests
{
    [Fact]
    public async Task SwallowedPostgreSqlLoyaltyErrorCannotUndoProviderSettlement()
    {
        var seed = await SeedAsync(42.50m, withUser: true);
        await using var context = _fixture.CreateContext();
        var visit = new TableServiceSession
        { Id = Guid.NewGuid(), TableNumber = 984, Currency = "CHF", OpenedAt = DateTime.UtcNow, CreatedBy = "test" };
        context.TableServiceSessions.Add(visit);
        (await context.Orders.SingleAsync()).ServiceSessionId = visit.Id;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var checkout = await context.OrderCheckoutSessions.AsNoTracking().SingleAsync();
        var fidelity = new Mock<IOrderFidelityCoordinator>();
        var sawSqlError = false;
        var outsideTransaction = false;
        fidelity.Setup(value => value.AwardEarnedPointsAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                outsideTransaction = context.Database.CurrentTransaction is null;
                try { await context.Database.ExecuteSqlRawAsync("SELECT 1 / 0"); }
                catch (PostgresException exception)
                {
                    exception.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);
                    sawSqlError = true; // Reproduces the coordinator's best-effort swallow.
                }
            });
        var current = CurrentUser();
        var writer = new CheckoutSettlementWriter(context, new OrderPaymentBuilder(current), fidelity.Object,
            new Mock<RestaurantSystem.Api.Features.Payments.Interfaces.ISettlementNotifier>().Object,
            current, NullLogger<CheckoutSettlementWriter>.Instance);
        await writer.SettleAsync(checkout, "pi_loyalty_failure", 4250, CancellationToken.None);
        sawSqlError.Should().BeTrue();
        outsideTransaction.Should().BeTrue();
        await using var read = _fixture.CreateContext();
        var saved = await read.OrderCheckoutSessions.AsNoTracking().SingleAsync();
        saved.Status.Should().Be(CheckoutSessionStatus.Completed);
        saved.OrderPaymentId.Should().NotBeNull();
        (await read.OrderPayments.SingleAsync(value => value.Id == saved.OrderPaymentId))
            .Status.Should().Be(PaymentStatus.Completed);
        (await read.Orders.SingleAsync(value => value.Id == seed.OrderId)).TotalPaid.Should().Be(42.50m);
        (await read.TableServiceSessions.SingleAsync()).AccountRevision.Should().Be(2);
        await writer.SettleAsync(checkout, "pi_loyalty_failure", 4250, CancellationToken.None);
        fidelity.Verify(value => value.AwardEarnedPointsAsync(It.IsAny<Order>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
