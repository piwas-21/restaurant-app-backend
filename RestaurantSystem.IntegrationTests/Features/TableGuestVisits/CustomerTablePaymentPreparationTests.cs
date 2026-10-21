using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePaymentPreparationTests(DatabaseFixture fixture) : CustomerTablePaymentPreparationFixture(fixture)
{
    [Theory]
    [InlineData(AccountPaymentMode.Full, 1400)]
    [InlineData(AccountPaymentMode.Amount, 350)]
    [InlineData(AccountPaymentMode.Items, 700)]
    public async Task Reviews_exact_server_scope_without_any_payment_effect(AccountPaymentMode mode, long expected)
    {
        var intent = await PaymentIntentAsync(mode);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(value => value.Items).SingleAsync();
        if (mode == AccountPaymentMode.Amount) intent = intent with { AmountMinor = 350 };
        if (mode == AccountPaymentMode.Items) intent = intent with
        { SelectedUnits = [new(order.Id, order.Items.Single().Id, 2)] };
        var prepared = await PreparedPaymentAsync(intent);
        prepared.SchemaVersion.Should().Be(1); prepared.Currency.Should().Be("CHF");
        prepared.ServiceSessionId.Should().Be(Visit.ServiceSessionId); prepared.AccountRevision.Should().Be(intent.ExpectedAccountRevision);
        prepared.Mode.Should().Be(mode); prepared.AmountMinor.Should().Be(expected);
        prepared.Allocations.Sum(row => row.AmountMinor).Should().Be(expected);
        PreparedScopeObserved.Should().BeTrue();
        Gateway.VerifyGet(value => value.IsConfigured, Times.AtLeastOnce);
        using var scope = Factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<ICustomerTablePaymentAccess>();
        var owner = access.Capture(Session, 1, NativeTenantId);
        var proofs = scope.ServiceProvider.GetRequiredService<ICustomerTablePaymentProofs>();
        var binding = proofs.Require(prepared.Proof, owner);
        binding.GuestOperationId.Should().NotBe(intent.OperationId);
        binding.AmountMinor.Should().Be(expected); binding.ScopeHash.Should().HaveLength(64);
        proofs.Invoking(value => value.RequireInput(intent, owner, binding, prepared.Proof)).Should().NotThrow();
        await NoPaymentEffectsAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reviews_an_existing_equal_or_custom_slot_without_claiming_it(bool custom)
    {
        var intent = await PaymentIntentAsync();
        using var scope = Factory.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<IAccountEqualSharePlanService>();
        var plan = await plans.CreateGuestAsync(Visit.ServiceSessionId, Visit.ParticipantToken,
            new()
            {
                OperationId = Guid.NewGuid(),
                ExpectedAccountRevision = intent.ExpectedAccountRevision,
                ShareCount = 2,
                CustomAmountsMinor = custom ? [1000, 400] : []
            }, CancellationToken.None);
        intent = custom ? intent with { Mode = AccountPaymentMode.CustomAmount, CustomSharePlanId = plan.PlanId, CustomShareOrdinal = 2 }
            : intent with { Mode = AccountPaymentMode.Equal, EqualSharePlanId = plan.PlanId, EqualShareOrdinal = 2 };
        (await PreparedPaymentAsync(intent)).AmountMinor.Should().Be(custom ? 400 : 700);
        await NoPaymentEffectsAsync();
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("tenantId")]
    [InlineData("operationId")]
    [InlineData("serviceSessionId")]
    [InlineData("expectedAccountRevision")]
    [InlineData("mode")]
    public async Task Requires_explicit_wire_identity_and_mode(string field)
    {
        var intent = await PaymentIntentAsync();
        var body = JsonSerializerNode(intent); body.Remove(field).Should().BeTrue();
        (await SendAsync(PreparationPath, body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await NoPaymentEffectsAsync();
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("session")]
    [InlineData("participant")]
    [InlineData("revision")]
    [InlineData("invalid-jwt")]
    [InlineData("staff")]
    public async Task Refuses_independent_authority_mismatches(string changed)
    {
        var intent = await PaymentIntentAsync();
        if (changed == "tenant") intent = intent with { TenantId = Guid.NewGuid() };
        if (changed == "revision") intent = intent with { ExpectedAccountRevision = intent.ExpectedAccountRevision + 1 };
        if (changed == "participant") Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        if (changed == "invalid-jwt") Client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
        if (changed == "staff") Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CustomerBasketTokens.Create(Factory.Services, UserRole.Admin));
        var response = await SendAsync(PreparationPath, intent, changed == "session" ? "bad-session" : null);
        response.IsSuccessStatusCode.Should().BeFalse(); PreparedScopeObserved.Should().BeFalse();
        await NoPaymentEffectsAsync();
    }

    [Theory]
    [InlineData("native")]
    [InlineData("accounts")]
    [InlineData("guest-feature")]
    [InlineData("hosted")]
    [InlineData("currency")]
    [InlineData("customer")]
    [InlineData("participant")]
    [InlineData("closed")]
    public async Task Withdraws_the_prepared_proof_after_actual_scope_read(string changed)
    {
        var customer = await SignInCustomerAsync();
        var intent = await PaymentIntentAsync();
        AfterProof = () =>
        {
            if (changed == "native") WithdrawNative();
            if (changed == "accounts") Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value.EnabledCapabilities = ["ordering", "table-visits", "table-payments"];
            if (changed == "guest-feature") Features.SetupGet(value => value.TableGuestAccountPaymentsV1).Returns(false);
            if (changed == "hosted") Factory.Services.GetRequiredService<IOptions<CustomerHostedCheckoutSettings>>().Value.Enabled = false;
            using var context = DatabaseFixture.CreateContext();
            if (changed == "currency") context.RestaurantInfo.First().Currency = "EUR";
            if (changed == "customer") context.Users.Single(value => value.Id == customer).IsDeleted = true;
            if (changed == "participant") context.TableGuestParticipants.Single().RevokedAt = DateTime.UtcNow;
            if (changed == "closed") context.TableServiceSessions.Single().Status = TableServiceSessionStatus.Closed;
            context.SaveChanges();
        };
        var response = await SendAsync(PreparationPath, intent);
        PreparedScopeObserved.Should().BeTrue("the real ledger selection and protected proof must precede withdrawal");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().NotContain("ownerProof");
        await NoPaymentEffectsAsync();
    }

    private static JsonObject JsonSerializerNode(CustomerTablePaymentIntentDto intent) =>
        System.Text.Json.JsonSerializer.SerializeToNode(intent, JsonOptions)!.AsObject();
}
