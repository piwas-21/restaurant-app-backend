using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

[Collection("Database Lane 3")]
public sealed partial class AccountCheckoutJournalTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly string Credential = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static readonly DateTime Now = new(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_frozen_request_and_receipt_commit_before_provider_creation()
    {
        var seed = await Seed();
        await using var context = DatabaseFixture.CreateContext();
        var provider = Provider();
        var policy = new Mock<IGuestAccountPaymentPolicy>(MockBehavior.Strict);
        policy.Setup(value => value.RequireContribution(334, "CHF"));
        var store = Store(context, seed.Actor, provider.Object, policy.Object);
        var journal = await store.FreezeGuestAsync(seed.SessionId, seed.OperationId, 1,
            "participant-credential", Credential, CancellationToken.None);
        context.Database.CurrentTransaction.Should().BeNull();
        provider.Verify(value => value.CreateAsync(It.IsAny<AccountStripeCheckoutRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
        await using var readback = DatabaseFixture.CreateContext();
        var saved = await readback.AccountCheckoutJournals.SingleAsync(value => value.Id == journal.Id);
        saved.AmountMinor.Should().Be(334);
        saved.ReturnBaseUrl.Should().Be("https://tenant.test/table-account");
        AccountReceiptCredentialCrypto.Verify(Credential, saved.ReceiptCredentialHash).Should().BeTrue();
        saved.ReceiptCredentialHash.Should().NotBe(Credential);
        var replay = AccountCheckoutReplayPayload.Read(saved, Now);
        replay.ExpiresAt.Should().Be(Now.AddHours(1));
        (await readback.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Starting);
    }

    [Fact]
    public async Task Feature_disabled_replay_preserves_the_original_request_and_cannot_replace_the_receipt()
    {
        var seed = await Seed();
        await using var context = DatabaseFixture.CreateContext();
        var policy = new Mock<IGuestAccountPaymentPolicy>();
        var store = Store(context, seed.Actor, Provider().Object, policy.Object);
        var first = await store.FreezeGuestAsync(seed.SessionId, seed.OperationId, 1,
            "participant-credential", Credential, CancellationToken.None);
        policy.Setup(value => value.RequireContribution(334, "CHF")).Throws(new NotFoundException("Disabled"));
        var retry = await store.FreezeGuestAsync(seed.SessionId, seed.OperationId, 1,
            "participant-credential", Credential, CancellationToken.None);
        retry.Id.Should().Be(first.Id);
        retry.CreateIdempotencyKey.Should().Be(first.CreateIdempotencyKey);
        policy.Verify(value => value.RequireContribution(334, "CHF"), Times.Once);
        var replacement = Convert.ToBase64String(Enumerable.Repeat((byte)8, 32).ToArray()).TrimEnd('=');
        var replace = () => store.FreezeGuestAsync(seed.SessionId, seed.OperationId, 1,
            "participant-credential", replacement, CancellationToken.None);
        await replace.Should().ThrowAsync<ConflictException>();
        (await context.AccountCheckoutJournals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Receipt_after_visit_close_exposes_only_this_contribution_and_expires()
    {
        var seed = await Seed();
        await using var context = DatabaseFixture.CreateContext();
        var journal = await Store(context, seed.Actor, Provider().Object, Mock.Of<IGuestAccountPaymentPolicy>())
            .FreezeGuestAsync(seed.SessionId, seed.OperationId, 1,
                "participant-credential", Credential, CancellationToken.None);
        var session = await context.TableServiceSessions.SingleAsync();
        session.Status = TableServiceSessionStatus.Closed;
        journal.ProviderCapturedMinor = 334;
        journal.ReconciliationRequired = true;
        await context.SaveChangesAsync();
        var reader = new AccountPaymentReceiptReader(context, new FixedClock());
        var receipt = await reader.ReadAsync(journal.AttemptId, Credential, CancellationToken.None);
        receipt.AttemptId.Should().Be(journal.AttemptId);
        receipt.AmountMinor.Should().Be(334);
        receipt.ReceivedMinor.Should().Be(334);
        receipt.ReconciliationRequired.Should().BeTrue();
        receipt.ReceiptExpiresAt.Should().Be(Now.AddHours(72),
            "the payer receives the authoritative receipt deadline, independent of visit closure");
        var fields = typeof(AccountPaymentReceiptDto).GetProperties().Select(value => value.Name).ToArray();
        fields.Should().NotContain(new[] { "ServiceSessionId", "Orders", "Allocations", "ProviderAccountId", "GuestName" });
        var other = () => reader.ReadAsync(Guid.NewGuid(), Credential, CancellationToken.None);
        await other.Should().ThrowAsync<NotFoundException>();
        journal.ReceiptExpiresAt = Now;
        await context.SaveChangesAsync();
        var expired = () => reader.ReadAsync(journal.AttemptId, Credential, CancellationToken.None);
        await expired.Should().ThrowAsync<NotFoundException>();
    }

    private AccountCheckoutJournalStore Store(RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context,
        Guid actorId, IAccountStripeCheckoutClient provider, IGuestAccountPaymentPolicy policy)
    {
        var authorization = new Mock<ITableGuestParticipantPaymentAuthorization>(MockBehavior.Strict);
        authorization.Setup(value => value.AuthorizeLockedAsync(It.IsAny<TableServiceSession>(),
            "participant-credential", It.IsAny<CancellationToken>())).ReturnsAsync(
            new AccountPaymentActor(actorId, AccountPaymentActorKind.GuestParticipant, "opaque-guest"));
        return new(context, authorization.Object, policy, provider,
            Options.Create(new AccountCheckoutSettings()), new FixedClock());
    }

    private static Mock<IAccountStripeCheckoutClient> Provider()
    {
        var provider = new Mock<IAccountStripeCheckoutClient>(MockBehavior.Strict);
        provider.Setup(value => value.ReadContext()).Returns(new AccountStripeContext("acct_journal", false));
        provider.Setup(value => value.ReadReturnBaseUrl()).Returns("https://tenant.test/table-account");
        return provider;
    }

    private async Task<(Guid SessionId, Guid OperationId, Guid Actor)> Seed(bool sharedScope = false)
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 987,
            Currency = "CHF",
            OpenedAt = Now,
            CreatedBy = "test"
        };
        var actor = Guid.NewGuid();
        var attempt = new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = session.Id,
            OperationId = Guid.NewGuid(),
            ActorId = actor,
            ActorKind = AccountPaymentActorKind.GuestParticipant,
            State = AccountPaymentState.Reserved,
            PaymentMethod = PaymentMethod.OnlinePayment,
            Mode = AccountPaymentMode.Amount,
            AmountMinor = 334,
            Currency = "CHF",
            QuoteExpiresAt = Now.AddMinutes(5),
            ReservationExpiresAt = Now.AddMinutes(5),
            ExpectedAccountRevision = 1,
            PayloadHash = "test",
            SnapshotJson = "{}",
            CreatedBy = "opaque-guest"
        };
        var orders = new List<Order>();
        if (sharedScope)
        {
            AddSharedOrder("CHECKOUT-A", 1.34m, 134);
            AddSharedOrder("CHECKOUT-B", 2.00m, 200);
        }
        else
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "CHECKOUT",
                ServiceSessionId = session.Id,
                Type = OrderType.DineIn,
                Status = OrderStatus.Completed,
                Total = 3.34m,
                RemainingAmount = 3.34m,
                OrderDate = Now,
                CreatedBy = "test"
            };
            orders.Add(order);
            attempt.Allocations.Add(new AccountPaymentAllocation
            {
                AttemptId = attempt.Id,
                OrderId = order.Id,
                StartOrdinal = 1,
                UnitCount = 1,
                MinorPerUnit = 334,
                AmountMinor = 334,
                CreatedBy = "opaque-guest"
            });
        }

        context.Add(session);
        context.AddRange(orders);
        context.Add(attempt);
        await context.SaveChangesAsync();
        return (session.Id, attempt.OperationId, actor);

        void AddSharedOrder(string orderNumber, decimal total, long amountMinor)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = orderNumber,
                ServiceSessionId = session.Id,
                Type = OrderType.DineIn,
                Status = OrderStatus.Completed,
                Total = total,
                RemainingAmount = total,
                OrderDate = Now,
                CreatedBy = "test"
            };
            var item = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductName = orderNumber,
                Quantity = 1,
                ItemTotal = total,
                CreatedBy = "test"
            };
            order.Items.Add(item);
            orders.Add(order);
            attempt.Allocations.Add(new AccountPaymentAllocation
            {
                AttemptId = attempt.Id,
                OrderId = order.Id,
                OrderItemId = item.Id,
                StartOrdinal = 1,
                UnitCount = 1,
                MinorPerUnit = amountMinor,
                AmountMinor = amountMinor,
                CreatedBy = "opaque-guest"
            });
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        // The frozen Stripe payload must survive PostgreSQL timestamp precision.
        public override DateTimeOffset GetUtcNow() => new(Now.AddTicks(34567));
    }
}
