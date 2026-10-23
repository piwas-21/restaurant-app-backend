using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public abstract class CustomerTableCheckoutFixture(DatabaseFixture fixture) : CustomerTablePaymentPreparationFixture(fixture)
{
    protected const string CheckoutPath = "/api/customer-mobile/table-payments/checkout/";
    protected const string Account = "acct_native_table_fixture";
    protected readonly List<AccountStripeCheckoutRequest> Creates = [];
    protected AccountStripeSession? Remote { get; set; }
    protected AccountStripeIntent? RemoteIntent { get; set; }
    protected AccountStripeCharge? RemoteCharge { get; set; }
    protected bool LoseFirstCreate { get; set; }
    protected Task? CreateWait { get; set; }
    protected int Gets { get; private set; }
    protected bool ProviderInsideTransaction { get; private set; }
    protected Action<AccountStripeCheckoutRequest>? BeforeCreate { get; set; }
    protected Action? AfterGet { get; set; }
    protected Action? BeforeExpire { get; set; }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<CustomerHostedCheckoutSettings>(value => value.HostedPageOrigins = ["https://checkout.stripe.com"]);
        services.RemoveAll<IAccountStripeCheckoutClient>();
        services.AddScoped<IAccountStripeCheckoutClient>(provider =>
        {
            var context = provider.GetRequiredService<ApplicationDbContext>();
            return new Provider(this, () => ProviderInsideTransaction |= context.Database.CurrentTransaction is not null);
        });
        var refunds = new Mock<IOrderAmendmentRefundProvider>(MockBehavior.Strict);
        refunds.Setup(value => value.ReadContext()).Returns(new AmendmentRefundProviderContext(Account, false));
        refunds.Setup(value => value.ListForChargeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AmendmentRefundEvidence>());
        services.RemoveAll<IOrderAmendmentRefundProvider>(); services.AddSingleton(refunds.Object);
    }

    protected async Task<CustomerTablePaymentPreparedDto> AcceptedAsync()
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        await PaymentResolutionAsync(await SendAsync("/api/customer-mobile/table-payments/submit",
            new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof }));
        return prepared;
    }
    protected async Task<CustomerTableCheckoutDto> CheckoutAsync(string action, CustomerTablePaymentProofDto proof)
    {
        var response = await SendAsync(CheckoutPath + action, proof);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerTableCheckoutDto>>(JsonOptions))!.Data!;
    }
    protected void Captured(string sessionStatus = "complete", string paymentStatus = "paid")
    {
        Remote = Remote! with { Status = sessionStatus, PaymentStatus = paymentStatus, IntentId = "pi_native_table" };
        RemoteIntent = new()
        {
            Id = "pi_native_table",
            Context = new(Account, false),
            Status = "succeeded",
            AmountMinor = 1400,
            ReceivedMinor = 1400,
            Currency = "chf",
            ChargeId = "ch_native_table",
            Metadata = Remote.Metadata
        };
        RemoteCharge = new()
        {
            Id = "ch_native_table",
            IntentId = "pi_native_table",
            Context = new(Account, false),
            Status = "succeeded",
            AmountMinor = 1400,
            CapturedMinor = 1400,
            RefundedMinor = 0,
            Currency = "chf",
            Paid = true,
            Captured = true,
            Disputed = false
        };
    }

    private sealed class Provider(CustomerTableCheckoutFixture owner, Action io) : IAccountStripeCheckoutClient
    {
        public AccountStripeContext ReadContext() => new(Account, false);
        public string ReadReturnBaseUrl() => throw new InvalidOperationException("Native checkout cannot use the web return path.");
        public async Task<AccountStripeSession> CreateAsync(AccountStripeCheckoutRequest request, CancellationToken cancellationToken)
        {
            io(); owner.Creates.Add(request); owner.BeforeCreate?.Invoke(request);
            if (owner.CreateWait is { } wait) await wait.WaitAsync(cancellationToken);
            owner.Remote ??= new()
            {
                Id = "cs_native_table",
                Context = new(Account, false),
                Status = "open",
                PaymentStatus = "unpaid",
                Url = "https://checkout.stripe.com/c/pay/native-table-fixture",
                AmountMinor = request.AmountMinor,
                Currency = "chf",
                ClientReferenceId = request.AttemptId.ToString("D"),
                Metadata = request.NativeScope!.Metadata(request.AttemptId)
            };
            if (owner.LoseFirstCreate && owner.Creates.Count == 1) throw new TimeoutException("Synthetic lost create response");
            return owner.Remote;
        }
        public Task<AccountStripeSession?> GetAsync(string sessionId, CancellationToken cancellationToken)
        { io(); owner.Gets++; owner.AfterGet?.Invoke(); return Task.FromResult(owner.Remote); }
        public Task<AccountStripeIntent?> GetIntentAsync(string intentId, CancellationToken cancellationToken)
        { io(); return Task.FromResult(owner.RemoteIntent); }
        public Task<AccountStripeCharge?> GetChargeAsync(string chargeId, CancellationToken cancellationToken)
        { io(); return Task.FromResult(owner.RemoteCharge); }
        public Task ExpireAsync(string sessionId, CancellationToken cancellationToken)
        {
            io(); owner.BeforeExpire?.Invoke();
            if (owner.Remote?.Status == "open") owner.Remote = owner.Remote with { Status = "expired", PaymentStatus = "unpaid", Url = null };
            return Task.CompletedTask;
        }
    }
}
