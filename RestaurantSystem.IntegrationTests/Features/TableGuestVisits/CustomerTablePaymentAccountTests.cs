using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePaymentAccountTests(DatabaseFixture fixture) : CustomerTablePaymentAccountFixture(fixture)
{
    [Fact]
    public async Task Guest_reads_the_server_ledger_without_enabling_or_creating_an_online_payment()
    {
        var intent = await RoundIntentAsync();
        var prepared = await PrepareRoundAsync(intent);
        await RoundResolutionAsync(await SendAsync(RoundPath + "submit", Submission(intent, prepared)));
        var response = await AccountResponseAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("participantToken").And.NotContain("actorId").And.NotContain("ownerProof")
            .And.NotContain("customerEmail").And.NotContain("providerSessionId").And.NotContain("receiptCredential");
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerTablePaymentAccountDto>>(JsonOptions))!.Data!;
        result.SchemaVersion.Should().Be(1);
        result.TenantId.Should().Be(NativeTenantId);
        result.Account.ServiceSessionId.Should().Be(Visit.ServiceSessionId);
        result.Account.AccountRevision.Should().BeGreaterThan(intent.ExpectedAccountRevision);
        result.Account.Currency.Should().Be("CHF");
        result.Account.OutstandingMinor.Should().Be(1400);
        result.Account.AvailableMinor.Should().Be(1400);
        result.Account.ReservedMinor.Should().Be(0);
        result.Account.CapturedAccountPaymentMinor.Should().Be(0);
        result.Account.AvailableAllocations.Should().ContainSingle().Which.MinorPerUnit.Should().Be(700);
        result.Account.Limits.Online.Should().BeNull();
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(0);
        (await context.AccountCheckoutJournals.CountAsync()).Should().Be(0);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_current_customer_still_supplies_the_independent_participant()
    {
        await SignInCustomerAsync();
        var response = await AccountResponseAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        (await AccountResponseAsync()).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reserved_contributions_keep_the_server_hold_and_hide_foreign_operation_identity(bool own)
    {
        var intent = await RoundIntentAsync();
        var prepared = await PrepareRoundAsync(intent);
        await RoundResolutionAsync(await SendAsync(RoundPath + "submit", Submission(intent, prepared)));
        var operation = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.Include(value => value.Items).SingleAsync();
            var actor = own ? (await context.TableGuestParticipants.SingleAsync()).Id : Guid.NewGuid();
            var attempt = new AccountPaymentAttempt
            {
                Id = Guid.NewGuid(),
                OperationId = operation,
                ServiceSessionId = Visit.ServiceSessionId,
                ActorId = actor,
                ActorKind = AccountPaymentActorKind.GuestParticipant,
                Mode = AccountPaymentMode.Items,
                State = AccountPaymentState.Reserved,
                Version = 1,
                PaymentMethod = PaymentMethod.OnlinePayment,
                ExpectedAccountRevision = intent.ExpectedAccountRevision + 1,
                AmountMinor = 700,
                Currency = "CHF",
                PayloadHash = new string('a', 64),
                QuoteExpiresAt = DateTime.UtcNow.AddMinutes(10),
                ReservationExpiresAt = DateTime.UtcNow.AddMinutes(10),
                CreatedBy = nameof(CustomerTablePaymentAccountTests)
            };
            attempt.Allocations.Add(new AccountPaymentAllocation
            {
                Id = Guid.NewGuid(),
                AttemptId = attempt.Id,
                OrderId = order.Id,
                OrderItemId = order.Items.Single().Id,
                StartOrdinal = 1,
                UnitCount = 1,
                MinorPerUnit = 700,
                AmountMinor = 700,
                CreatedBy = nameof(CustomerTablePaymentAccountTests)
            });
            attempt.SnapshotJson = AccountPaymentSnapshots.Serialize(new AccountPaymentQuoteSnapshot(
                attempt.ExpectedAccountRevision, attempt.Mode, attempt.PaymentMethod, 700, "CHF", attempt.QuoteExpiresAt,
                null, null, [new(order.Id, order.Items.Single().Id, 1, 1, 700, 700)]));
            context.AccountPaymentAttempts.Add(attempt);
            await context.SaveChangesAsync();
        }
        var response = await AccountResponseAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var account = (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerTablePaymentAccountDto>>(JsonOptions))!.Data!.Account;
        account.OutstandingMinor.Should().Be(1400);
        account.ReservedMinor.Should().Be(700);
        account.AvailableMinor.Should().Be(700);
        account.ActiveAttempts.Should().ContainSingle().Which.OperationId.Should().Be(own ? operation : null);
        account.ActiveAttempts[0].IsOwnOperation.Should().Be(own);
        if (!own) (await response.Content.ReadAsStringAsync()).Should().NotContain(operation.ToString("D"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("bad")]
    public async Task Version_is_explicit_and_supported(string? schema)
    {
        (await AccountResponseAsync(schema)).IsSuccessStatusCode.Should().BeFalse();
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("visit")]
    [InlineData("session")]
    [InlineData("participant")]
    public async Task Independent_native_and_participant_credentials_are_required(string changed)
    {
        if (changed == "participant") Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        var response = await AccountResponseAsync(tenant: changed == "tenant" ? Guid.NewGuid() : null,
            visit: changed == "visit" ? Guid.NewGuid() : null, session: changed == "session" ? "invalid-session" : null);
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("staff")]
    [InlineData("deleted")]
    public async Task Authorization_cannot_downgrade_to_a_valid_guest_participant(string identity)
    {
        var customer = await SignInCustomerAsync();
        if (identity == "invalid") Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        if (identity == "staff") Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CustomerBasketTokens.Create(Factory.Services, UserRole.Admin));
        if (identity == "deleted")
        {
            await using var context = DatabaseFixture.CreateContext();
            (await context.Users.SingleAsync(value => value.Id == customer)).IsDeleted = true;
            await context.SaveChangesAsync();
        }
        (await AccountResponseAsync()).IsSuccessStatusCode.Should().BeFalse();
    }

    [Theory]
    [InlineData("native")]
    [InlineData("accounts")]
    [InlineData("feature")]
    [InlineData("currency")]
    [InlineData("customer")]
    [InlineData("participant")]
    [InlineData("closed")]
    public async Task Withdrawal_after_the_actual_shared_ledger_read_hides_the_snapshot(string changed)
    {
        var customer = await SignInCustomerAsync();
        var observedRead = false;
        AfterRead = async () =>
        {
            observedRead = true;
            if (changed == "native") WithdrawNative();
            if (changed == "accounts")
                Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value
                    .EnabledCapabilities = ["ordering", "table-visits"];
            if (changed == "feature") Features.SetupGet(value => value.TableAccountPaymentsV1).Returns(false);
            await using var context = DatabaseFixture.CreateContext();
            if (changed == "currency") (await context.RestaurantInfo.FirstAsync()).Currency = "EUR";
            if (changed == "customer") (await context.Users.SingleAsync(value => value.Id == customer)).IsDeleted = true;
            if (changed == "participant") (await context.TableGuestParticipants.SingleAsync()).RevokedAt = DateTime.UtcNow;
            if (changed == "closed") (await context.TableServiceSessions.SingleAsync()).Status = TableServiceSessionStatus.Closed;
            await context.SaveChangesAsync();
        };
        var response = await AccountResponseAsync();
        observedRead.Should().BeTrue("withdrawal must happen after an actual authorized ledger read");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().NotContain("outstandingMinor");
    }
}
