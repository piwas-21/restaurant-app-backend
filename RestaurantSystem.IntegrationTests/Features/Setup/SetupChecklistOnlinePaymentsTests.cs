using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Setup;
using RestaurantSystem.Api.Features.Setup.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Setup;

/// <summary>
/// The <c>online-payments</c> checklist step (SOFRA-PAYMENTS-PLAN §9 P5).
///
/// <para>
/// Two things are under test, and neither is "does a row appear". The first is that the
/// step is entitled on <b>Stripe being configured</b> and not on the module flag alone,
/// because that flag fails OPEN on the live fleet: RUMI ships no <c>Modules:Enabled</c>
/// list, <c>TenantModules</c> reads that as unrestricted, and a step gated on it alone
/// would land on the checklist of a restaurant that cannot take a card and has no way to
/// ever tick it. That is the same hole S8 closed on the availability endpoint.
/// </para>
/// <para>
/// The second is that the step is DERIVED from a settled checkout session — money having
/// moved — rather than from configuration. A tenant is configured days before Stripe
/// finishes verifying their business, so a step ticked on <c>IsConfigured</c> would
/// congratulate them for something that has not happened yet, which is the one thing this
/// checklist exists to prevent.
/// </para>
/// </summary>
public abstract class SetupChecklistOnlinePaymentsTestsBase : IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;
    private TestWebApplicationFactory _factory = null!;

    protected SetupChecklistOnlinePaymentsTestsBase(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture ?? throw new ArgumentNullException(nameof(databaseFixture));
    }

    protected const string Url = "/api/admin/setup-checklist";

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>The configuration THIS class's tenant runs with.</summary>
    protected abstract IReadOnlyDictionary<string, string> Settings { get; }

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_databaseFixture.ConnectionString, Settings);
        Client = _factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Test-Admin", "true");

        await _databaseFixture.ResetDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        await TestDataSeeder.SeedBasicDataAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    protected async Task<SetupChecklistDto> GetChecklistAsync()
    {
        var response = await Client.GetAsync(Url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonSerializer
            .Deserialize<ApiResponse<SetupChecklistDto>>(
                await response.Content.ReadAsStringAsync(), JsonOptions)!.Data!;
    }

    /// <summary>A tenant whose Stripe credentials actually landed. Fake key; no network is made.</summary>
    protected static Dictionary<string, string> StripeConfigured() => new()
    {
        ["Stripe:Enabled"] = "true",
        ["Stripe:PlatformApiKey"] = "rk_test_setup_checklist",  // pragma: allowlist secret
        ["Stripe:ConnectedAccountId"] = "acct_setup_checklist",
    };

    /// <summary>
    /// A settled Stripe checkout for a real order — the ONLY observation that ticks this step.
    /// </summary>
    protected async Task SeedSettledCheckoutAsync()
    {
        await using var seed = _databaseFixture.CreateContext();

        var order = new Order
        {
            OrderNumber = $"P5-{Guid.NewGuid():N}"[..12],
            Type = OrderType.DineIn,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 12.00m,
            Total = 12.00m,
            TotalPaid = 12.00m,
            CustomerEmail = "diner@example.com",
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(SetupChecklistOnlinePaymentsTestsBase),
        };
        seed.Orders.Add(order);
        await seed.SaveChangesAsync();

        seed.OrderCheckoutSessions.Add(new OrderCheckoutSession
        {
            OrderId = order.Id,
            SessionId = $"cs_test_{Guid.NewGuid():N}",
            Status = CheckoutSessionStatus.Completed,
            Currency = "chf",
            AmountMinor = 1200,
            AmountReceivedMinor = 1200,
            IdempotencyKey = $"checkout:{order.Id}:1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            ConnectedAccountId = "acct_setup_checklist",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(SetupChecklistOnlinePaymentsTestsBase),
        });
        await seed.SaveChangesAsync();
    }
}

/// <summary>
/// RUMI's shape: enforcement on with an EMPTY module list, which <c>TenantModules</c> reads as
/// unrestricted, and no Stripe configuration at all. The module gate lets this tenant through.
/// </summary>
[Collection("Database Lane 2")]
public class SetupChecklistOnlinePaymentsUnconfiguredTests : SetupChecklistOnlinePaymentsTestsBase
{
    public SetupChecklistOnlinePaymentsUnconfiguredTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Modules:Enforce"] = "true",
        ["Modules:Enabled"] = "",
    };

    [Fact]
    public async Task TheModuleGateLetsThisTenantThroughAndTheStepIsStillAbsent()
    {
        // (a) in the slice. Drop the IsConfigured conjunct and this is the test that fails:
        // RUMI's owner gets a "take card payments" row they can neither use nor complete.
        var checklist = await GetChecklistAsync();

        checklist.Steps.Should().NotContain(s => s.Key == SetupSteps.OnlinePayments);
        // The control: every other unrestricted module step IS offered, so the absence
        // above is this step's own gate rather than a checklist that returned nothing.
        checklist.Steps.Should().Contain(s => s.Key == SetupSteps.Printing);
        checklist.Steps.Should().Contain(s => s.Key == SetupSteps.RestaurantInfo);
    }

    [Fact]
    public async Task AndTheWriteIsRefusedToo()
    {
        // Read and write are one gate. Were the write not gated as well, a POST here would
        // be refused only by the derived-step check — which is a different rule that could
        // be relaxed independently, leaving an unentitled acknowledgement storable again.
        var ack = await Client.PutAsJsonAsync(
            $"{Url}/steps/{SetupSteps.OnlinePayments}", new SetStepDoneRequest { IsDone = true });

        ack.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

/// <summary>
/// A tenant that bought the module AND whose Stripe credentials reached the box — but who has
/// not yet been paid by anybody. The normal state of a restaurant mid-KYC.
/// </summary>
[Collection("Database Lane 2")]
public class SetupChecklistOnlinePaymentsConfiguredTests : SetupChecklistOnlinePaymentsTestsBase
{
    public SetupChecklistOnlinePaymentsConfiguredTests(DatabaseFixture databaseFixture)
        : base(databaseFixture) { }

    protected override IReadOnlyDictionary<string, string> Settings
    {
        get
        {
            var settings = StripeConfigured();
            settings["Modules:Enforce"] = "true";
            settings["Modules:Enabled"] = "core,online-payments";
            return settings;
        }
    }

    [Fact]
    public async Task ConfiguredButUnpaidShowsTheStepAsDerivedAndNotDone()
    {
        // (b). Configuration is NOT the observation — this tenant is configured and the
        // row still reads "not done", because no diner has paid them yet.
        var checklist = await GetChecklistAsync();

        var step = checklist.Steps.Single(s => s.Key == SetupSteps.OnlinePayments);
        step.IsDerived.Should().BeTrue();
        step.IsDone.Should().BeFalse();
        step.ModuleId.Should().Be("online-payments");
    }

    [Fact]
    public async Task TheStepCannotBeTickedByHand()
    {
        // (c). It is derived, so an acknowledgement is refused rather than ignored — the
        // same bargain `menu` and `staff` already strike. An owner who wants the checklist
        // gone without taking a payment dismisses the whole thing, which is honest.
        var ack = await Client.PutAsJsonAsync(
            $"{Url}/steps/{SetupSteps.OnlinePayments}", new SetStepDoneRequest { IsDone = true });

        ack.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetChecklistAsync()).Steps
            .Single(s => s.Key == SetupSteps.OnlinePayments).IsDone.Should().BeFalse();
    }

    [Fact]
    public async Task OneSettledCheckoutSessionTicksItAndRaisesTheCount()
    {
        // (d). The same GET, before and after one row — so the delta is the observation
        // itself and not any difference between two hosts.
        var before = await GetChecklistAsync();
        before.Steps.Single(s => s.Key == SetupSteps.OnlinePayments).IsDone.Should().BeFalse();

        await SeedSettledCheckoutAsync();

        var after = await GetChecklistAsync();
        after.Steps.Single(s => s.Key == SetupSteps.OnlinePayments).IsDone.Should().BeTrue();
        after.DoneCount.Should().Be(before.DoneCount + 1);
    }
}
