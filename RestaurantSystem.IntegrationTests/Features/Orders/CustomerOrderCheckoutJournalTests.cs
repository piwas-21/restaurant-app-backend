using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutJournalTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    [Fact]
    public async Task Exact_original_request_is_durable_before_any_provider_call_and_survives_configuration_changes()
    {
        var id = await SeedAsync();
        Commission.Bps = 150;
        var first = await FreezeAsync();
        first.OrderId.Should().Be(id);
        first.OperationId.Should().Be(Operation);
        first.StartedAt.Should().Be(new DateTime(2026, 10, 19, 19, 0, 0, DateTimeKind.Utc));
        first.ExpiresAt.Should().Be(first.StartedAt.AddMinutes(31));
        first.MaximumCreateRetryAt.Should().Be(first.StartedAt.AddHours(23));
        first.AmountMinor.Should().Be(1234);
        first.ApplicationFeeMinor.Should().Be(19);
        first.Currency.Should().Be("chf");
        first.ProviderSessionId.Should().BeNull();
        first.LeaseId.Should().BeNull();
        await using var read = Database.CreateContext();
        var stored = await read.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync();
        stored.Should().BeEquivalentTo(first);
        (await read.OrderCheckoutSessions.CountAsync()).Should().Be(0);
        (await read.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
        var original = CustomerOrderCheckoutReplay.Read(stored, first.StartedAt, forCreate: true);
        Settings.LinkOrigin = "https://changed.example.test";
        Settings.Enabled = false;
        Commission.Bps = 1000;
        Localization.Currency = "EUR";
        ProviderReady = false;
        OnlineCapability = "disabled";
        Clock.Now = Clock.Now.AddDays(2);
        var recovered = await FreezeAsync();
        recovered.Should().BeEquivalentTo(first);
        CustomerOrderCheckoutReplay.Read(recovered, Clock.GetUtcNow().UtcDateTime, forCreate: false)
            .Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task Concurrent_recovery_freezes_one_request_and_one_identity()
    {
        await SeedAsync();
        var results = await Task.WhenAll(FreezeAsync(), FreezeAsync());
        results[0].Should().BeEquivalentTo(results[1]);
        await using var read = Database.CreateContext();
        (await read.Set<CustomerOrderCheckoutJournal>().CountAsync()).Should().Be(1);
        (await read.OrderCheckoutSessions.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("module")]
    [InlineData("provider")]
    [InlineData("currency")]
    [InlineData("money")]
    [InlineData("intent")]
    public async Task New_checkout_refuses_unavailable_or_changed_financial_state_without_a_journal(string scenario)
    {
        var id = await SeedAsync();
        await using (var write = Database.CreateContext())
        {
            switch (scenario)
            {
                case "disabled": Settings.Enabled = false; break;
                case "module": OnlineCapability = "disabled"; break;
                case "provider": ProviderReady = false; break;
                case "currency": Localization.Currency = "EUR"; break;
                // Accepted facts are immutable in PostgreSQL. Make the provider-amount dependency
                // hostile directly rather than attempting an impossible original-total mutation.
                case "money": ChangedCharge = true; break;
                case "intent": (await write.OrderPayments.SingleAsync()).Status = PaymentStatus.Failed; break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            await write.SaveChangesAsync();
        }
        var invoke = () => FreezeAsync();
        if (scenario is "disabled" or "module") await invoke.Should().ThrowAsync<NotFoundException>();
        else if (scenario is "provider" or "intent") await invoke.Should().ThrowAsync<BadRequestException>();
        else await invoke.Should().ThrowAsync<ConflictException>();
        await AssertNoCheckoutAsync();
    }

    [Fact]
    public async Task Original_private_proof_is_required_even_when_a_journal_already_exists()
    {
        await SeedAsync();
        var positive = await FreezeAsync();
        await FluentActions.Invoking(() => FreezeAsync(session: Guid.NewGuid().ToString("D")))
            .Should().ThrowAsync<NotFoundException>();
        await FluentActions.Invoking(() => FreezeAsync(proof: Proof with { PayloadHash = new string('b', 64) }))
            .Should().ThrowAsync<ConflictException>();
        (await FreezeAsync()).Should().BeEquivalentTo(positive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unseen_or_cancelled_order_operation_cannot_prepare_payment(bool cancelled)
    {
        if (cancelled)
        {
            await using var context = Database.CreateContext();
            await Store(context).CancelAsync(Session, Operation, Payload, CancellationToken.None);
        }
        await FluentActions.Invoking(() => FreezeAsync()).Should().ThrowAsync<NotFoundException>();
        await AssertNoCheckoutAsync();
    }

    [Fact]
    public async Task An_existing_legacy_session_prevents_a_second_native_provider_rail()
    {
        var id = await SeedAsync();
        await using (var write = Database.CreateContext())
        {
            write.OrderCheckoutSessions.Add(new()
            {
                OrderId = id,
                SessionId = "cs_existing_web_fixture",
                Currency = "chf",
                AmountMinor = 1234,
                ConnectedAccountId = "acct_native_fixture",
                IdempotencyKey = "existing-web-fixture",
                ExpiresAt = Clock.GetUtcNow().UtcDateTime.AddMinutes(31),
                CreatedBy = "native-checkout-fixture"
            });
            await write.SaveChangesAsync();
        }
        await FluentActions.Invoking(() => FreezeAsync()).Should().ThrowAsync<ConflictException>();
        await using var read = Database.CreateContext();
        (await read.Set<CustomerOrderCheckoutJournal>().CountAsync()).Should().Be(0);
        (await read.OrderCheckoutSessions.SingleAsync()).SessionId.Should().Be("cs_existing_web_fixture");
    }

    [Fact]
    public async Task Original_customer_proof_cannot_read_an_order_unlinked_from_a_still_active_account()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner);
        await FreezeAsync(owner);
        await using (var write = Database.CreateContext())
        {
            (await write.Orders.SingleAsync(value => value.Id == id)).UserId = null;
            await write.SaveChangesAsync();
            (await write.Users.SingleAsync(value => value.Id == owner)).IsDeleted.Should().BeFalse();
        }
        await FluentActions.Invoking(() => FreezeAsync(owner)).Should().ThrowAsync<NotFoundException>();
        await using var read = Database.CreateContext();
        (await read.Set<CustomerOrderCheckoutJournal>().CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_customer_proof_cannot_read_after_database_role_or_account_withdrawal(bool deleted)
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner);
        await FreezeAsync(owner);
        await using (var write = Database.CreateContext())
        {
            var user = await write.Users.SingleAsync(value => value.Id == owner);
            if (deleted) user.IsDeleted = true;
            else user.Role = UserRole.Cashier;
            await write.SaveChangesAsync();
        }
        await FluentActions.Invoking(() => FreezeAsync(owner)).Should().ThrowAsync<NotFoundException>();
        await using var read = Database.CreateContext();
        (await read.Set<CustomerOrderCheckoutJournal>().CountAsync()).Should().Be(1);
    }
}
