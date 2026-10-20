using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutLeaseTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    private Task<CustomerOrderCheckoutLease> AcquireAsync(Guid? owner = null) =>
        WithCheckoutAsync((_, _, leases) => leases.AcquireAsync(Session, Proof, CancellationToken.None), owner);

    private Task<bool> RecordAsync(CustomerOrderCheckoutLease lease, StripeCheckoutSession evidence, Guid? owner = null) =>
        WithCheckoutAsync(async (_, _, leases) =>
        {
            await leases.RecordAsync(Session, Proof, lease, evidence, CancellationToken.None);
            return true;
        }, owner);

    [Fact]
    public async Task One_concurrent_worker_acquires_the_durable_lease_and_expiry_reuses_the_exact_original_request()
    {
        await SeedAsync();
        var attempts = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        attempts.Count(value => value is not null).Should().Be(1);
        var first = attempts.Single(value => value is not null)!;
        await using var read = Database.CreateContext();
        var stored = await read.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync();
        stored.LeaseId.Should().Be(first.Id);
        stored.LeaseExpiresAt.Should().Be(Clock.Now.UtcDateTime.AddMinutes(1));
        Clock.Now = Clock.Now.AddMinutes(1);
        var retry = await AcquireAsync();
        retry.Id.Should().NotBe(first.Id);
        retry.Request.Should().BeEquivalentTo(first.Request);
        retry.ProviderSessionId.Should().BeNull();
        await FluentActions.Invoking(() => RecordAsync(first, Evidence(first)))
            .Should().ThrowAsync<ConflictException>();
        (await read.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync()).LeaseId.Should().Be(retry.Id);

        async Task<CustomerOrderCheckoutLease?> AttemptAsync()
        {
            try { return await AcquireAsync(); }
            catch (ConflictException) { return null; }
        }
    }

    [Fact]
    public async Task Verified_reference_is_durable_and_recovery_reads_it_after_creation_withdrawal_and_deadline()
    {
        await SeedAsync();
        var first = await AcquireAsync();
        await RecordAsync(first, Evidence(first));
        Settings.Enabled = false;
        OnlineCapability = "disabled";
        Clock.Now = Clock.Now.AddDays(2);
        var read = await AcquireAsync();
        read.ProviderSessionId.Should().Be("cs_native_canonical_fixture");
        read.Request.Should().BeEquivalentTo(first.Request);
        await using var context = Database.CreateContext();
        var stored = await context.Set<CustomerOrderCheckoutJournal>().SingleAsync();
        stored.LastVerifiedAt.Should().Be(Clock.Now.AddDays(-2).UtcDateTime);
        (await context.OrderCheckoutSessions.CountAsync()).Should().Be(0);
        (await context.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("module")]
    [InlineData("closed")]
    [InlineData("deadline")]
    [InlineData("unlinked")]
    public async Task Unknown_create_cannot_be_reissued_after_withdrawal_or_owner_change(string scenario)
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner);
        var first = await AcquireAsync(owner);
        Clock.Now = Clock.Now.AddMinutes(2);
        await using (var write = Database.CreateContext())
        {
            switch (scenario)
            {
                case "settings": Settings.Enabled = false; break;
                case "module": OnlineCapability = "disabled"; break;
                case "closed": (await write.Orders.SingleAsync()).Status = OrderStatus.Cancelled; break;
                case "deadline": Clock.Now = Clock.Now.AddDays(1); break;
                case "unlinked": (await write.Orders.SingleAsync()).UserId = null; break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            await write.SaveChangesAsync();
        }
        var attempt = FluentActions.Invoking(() => AcquireAsync(owner));
        if (scenario == "closed") await attempt.Should().ThrowAsync<BadRequestException>();
        else if (scenario == "deadline") await attempt.Should().ThrowAsync<ConflictException>();
        else await attempt.Should().ThrowAsync<NotFoundException>();
        await using var read = Database.CreateContext();
        var stored = await read.Set<CustomerOrderCheckoutJournal>().SingleAsync();
        stored.CreatePayloadHash.Should().Be(CustomerOrderCheckoutReplay.Hash(first.Request));
        stored.ProviderSessionId.Should().BeNull();
        stored.LeaseId.Should().Be(first.Id);
        stored.LastVerifiedAt.Should().BeNull();
        (await read.OrderPayments.SingleAsync()).Status.Should().Be(PaymentStatus.Processing);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("mode")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("operation")]
    [InlineData("schema")]
    [InlineData("order")]
    [InlineData("session")]
    public async Task Mismatched_canonical_evidence_cannot_publish_or_release_the_lease(string scenario)
    {
        await SeedAsync();
        var lease = await AcquireAsync();
        var evidence = Evidence(lease);
        evidence = scenario switch
        {
            "account" => evidence with { ProviderContext = new("acct_other", false) },
            "mode" => evidence with { ProviderContext = new("acct_native_fixture", true) },
            "amount" => evidence with { AmountTotalMinor = 1235 },
            "currency" => evidence with { Currency = "eur" },
            "operation" => evidence with
            {
                Metadata = new Dictionary<string, string>
                {
                    [NativeCheckoutRouting.OperationMetadataKey] = Guid.NewGuid().ToString("D"),
                    [NativeCheckoutRouting.SchemaMetadataKey] = NativeCheckoutRouting.SchemaVersion
                }
            },
            "schema" => evidence with
            {
                Metadata = new Dictionary<string, string>
                { [NativeCheckoutRouting.OperationMetadataKey] = Operation.ToString("D") }
            },
            "order" => evidence with { ClientReferenceId = Guid.NewGuid().ToString("D") },
            "session" => evidence with { Id = "invalid" },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        await FluentActions.Invoking(() => RecordAsync(lease, evidence)).Should().ThrowAsync<ConflictException>();
        await using var read = Database.CreateContext();
        var stored = await read.Set<CustomerOrderCheckoutJournal>().SingleAsync();
        stored.LeaseId.Should().Be(lease.Id);
        stored.ProviderSessionId.Should().BeNull();
        stored.LastVerifiedAt.Should().BeNull();
        // Positive control: the same outstanding lease accepts the original complete identity.
        await RecordAsync(lease, Evidence(lease));
    }

    [Fact]
    public async Task Expired_worker_and_wrong_private_proof_cannot_record_even_valid_provider_evidence()
    {
        await SeedAsync();
        var lease = await AcquireAsync();
        await FluentActions.Invoking(() => WithCheckoutAsync(async (_, _, leases) =>
        {
            await leases.RecordAsync(Guid.NewGuid().ToString("D"), Proof, lease, Evidence(lease), CancellationToken.None);
            return true;
        })).Should().ThrowAsync<NotFoundException>();
        Clock.Now = Clock.Now.AddMinutes(1);
        await FluentActions.Invoking(() => RecordAsync(lease, Evidence(lease))).Should().ThrowAsync<ConflictException>();
        await using var read = Database.CreateContext();
        var stored = await read.Set<CustomerOrderCheckoutJournal>().SingleAsync();
        stored.LeaseId.Should().Be(lease.Id);
        stored.ProviderSessionId.Should().BeNull();
    }

    internal static StripeCheckoutSession Evidence(CustomerOrderCheckoutLease lease) => Evidence(lease.Request);

    internal static StripeCheckoutSession Evidence(CheckoutSessionRequest request) => new()
    {
        Id = "cs_native_canonical_fixture",
        Status = "open",
        PaymentStatus = "unpaid",
        Url = "https://checkout.stripe.com/fixture",
        ProviderContext = new("acct_native_fixture", false),
        AmountTotalMinor = 1234,
        Currency = "chf",
        ClientReferenceId = request.OrderId.ToString("D"),
        Metadata = request.NativeRouting!.Metadata()
    };
}
