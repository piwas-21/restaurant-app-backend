using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutEffectsTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    [Fact]
    public async Task Queue_or_inflight_claim_is_not_completion_and_background_replay_waits_for_both_sent_ledgers()
    {
        var id = await SeedAsync();
        await WithCheckoutAsync(async (_, _, leases) =>
        {
            var lease = await leases.AcquireAsync(Session, Proof, CancellationToken.None);
            await leases.RecordAsync(Session, Proof, lease, CustomerOrderCheckoutSettlementTests.Paid(lease), CancellationToken.None);
            return true;
        });
        await using var context = Database.CreateContext();
        var mapping = new Mock<IOrderMappingService>();
        var view = new OrderDto { Id = id, OrderNumber = "native-effects-fixture", CustomerEmail = "native-effects@example.test" };
        mapping.Setup(value => value.MapToOrderDtoAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>())).ReturnsAsync(view);
        var guest = new Mock<IGuestOrderReceiptSender>(MockBehavior.Strict);
        var admin = new Mock<IAdminOrderAlertSender>(MockBehavior.Strict);
        guest.Setup(value => value.Queue(view));
        admin.Setup(value => value.Queue(view));
        var worker = new CustomerOrderCheckoutEffects(context, Mock.Of<IFidelityPointsService>(), mapping.Object,
            guest.Object, admin.Object, Clock);
        await worker.CompleteAsync(id, CancellationToken.None);
        (await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync()).EffectsCompletedAt.Should().BeNull();
        context.OutboundEmails.AddRange(new OutboundEmail
        {
            EmailType = OutboundEmailTypes.OrderAdminAlert,
            EntityId = id,
            CreatedBy = "effects-fixture",
            SentAt = Clock.Now.UtcDateTime
        },
            new OutboundEmail { EmailType = OutboundEmailTypes.OrderReceived, EntityId = id, CreatedBy = "effects-fixture" });
        await context.SaveChangesAsync();
        await worker.CompleteAsync(id, CancellationToken.None);
        (await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync()).EffectsCompletedAt.Should().BeNull();
        await context.OutboundEmails.ExecuteUpdateAsync(update => update.SetProperty(value => value.SentAt, Clock.Now.UtcDateTime));
        var replacementLease = Guid.NewGuid();
        await context.Set<CustomerOrderCheckoutJournal>().ExecuteUpdateAsync(update => update
            .SetProperty(value => value.LeaseId, replacementLease)
            .SetProperty(value => value.LeaseExpiresAt, Clock.Now.AddMinutes(2).UtcDateTime));
        await worker.CompleteAsync(id, CancellationToken.None);
        var completed = await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync();
        completed.EffectsCompletedAt.Should().Be(Clock.Now.UtcDateTime);
        completed.LeaseId.Should().Be(replacementLease);
        completed.LeaseExpiresAt.Should().Be(Clock.Now.AddMinutes(2).UtcDateTime);
        (await context.Orders.AsNoTracking().SingleAsync()).TotalPaid.Should().Be(12.34m);
    }

    [Fact]
    public async Task Strict_loyalty_failure_and_deferral_keep_effects_recoverable()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner);
        await WithCheckoutAsync(async (_, _, leases) =>
        {
            var lease = await leases.AcquireAsync(Session, Proof, CancellationToken.None);
            await leases.RecordAsync(Session, Proof, lease, CustomerOrderCheckoutSettlementTests.Paid(lease), CancellationToken.None);
            return true;
        }, owner);
        await using var context = Database.CreateContext();
        var fidelity = new Mock<IFidelityPointsService>(MockBehavior.Strict);
        fidelity.Setup(value => value.AwardAcceptedOrderAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("injected award failure"));
        var mapping = new Mock<IOrderMappingService>();
        var view = new OrderDto { Id = id, OrderNumber = "loyalty-effects-fixture" };
        mapping.Setup(value => value.MapToOrderDtoAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>())).ReturnsAsync(view);
        var guest = new Mock<IGuestOrderReceiptSender>();
        var admin = new Mock<IAdminOrderAlertSender>();
        var worker = new CustomerOrderCheckoutEffects(context, fidelity.Object, mapping.Object, guest.Object, admin.Object, Clock);
        await FluentActions.Invoking(() => worker.CompleteAsync(id, CancellationToken.None)).Should().ThrowAsync<IOException>();
        guest.Verify(value => value.Queue(view), Times.Once);
        admin.Verify(value => value.Queue(view), Times.Once);
        (await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync()).EffectsCompletedAt.Should().BeNull();
        context.OutboundEmails.Add(new OutboundEmail
        {
            EmailType = OutboundEmailTypes.OrderAdminAlert,
            EntityId = id,
            CreatedBy = "loyalty-effects-fixture",
            SentAt = Clock.Now.UtcDateTime
        });
        await context.SaveChangesAsync();
        fidelity.Setup(value => value.AwardAcceptedOrderAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FidelityPointsAwardResult(FidelityPointsAwardDisposition.Deferred,
                FidelityPointsAwardDeferralReason.CandidateUnevaluated, null, null, null));
        await worker.CompleteAsync(id, CancellationToken.None);
        (await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync()).EffectsCompletedAt.Should().BeNull();
        fidelity.Setup(value => value.AwardAcceptedOrderAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FidelityPointsAwardResult(FidelityPointsAwardDisposition.EvaluatedZero, null, 0, 0, 0));
        await worker.CompleteAsync(id, CancellationToken.None);
        (await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync()).EffectsCompletedAt.Should().Be(Clock.Now.UtcDateTime);
    }
}
