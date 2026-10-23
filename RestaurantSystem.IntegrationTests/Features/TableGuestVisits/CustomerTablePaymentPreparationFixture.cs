using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public abstract class CustomerTablePaymentPreparationFixture(DatabaseFixture fixture) : CustomerTableRoundFixture(fixture)
{
    protected const string PreparationPath = "/api/customer-mobile/table-payments/prepare";
    protected bool PreparedScopeObserved { get; private set; }
    protected Action? AfterProof { get; set; }
    protected Mock<IStripeGateway> Gateway { get; } = new(MockBehavior.Strict);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        Features.SetupGet(value => value.TableAccountPaymentsV1).Returns(true);
        Features.SetupGet(value => value.TableGuestAccountPaymentsV1).Returns(true);
        services.PostConfigure<CustomerMobileSettings>(value =>
            value.EnabledCapabilities = ["ordering", "accounts", "table-visits", "table-payments"]);
        services.PostConfigure<CustomerHostedCheckoutSettings>(value =>
        {
            value.Enabled = true; value.LinkOrigin = "https://links.example.test";
            value.TenantSlug = "fixture"; value.HostedPageOrigins = ["https://checkout.example.test"];
        });
        services.PostConfigure<AccountOnlineContributionSettings>(value => value.SettlementCurrency = "CHF");
        var modules = new Mock<ITenantModules>();
        modules.SetupGet(value => value.IsEnforced).Returns(true);
        modules.SetupGet(value => value.EnabledModules).Returns([ModuleIds.Core, ModuleIds.Server, ModuleIds.OnlinePayments]);
        modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns((string id) =>
            id is ModuleIds.Core or ModuleIds.Server or ModuleIds.OnlinePayments);
        services.RemoveAll<ITenantModules>(); services.AddSingleton(modules.Object);
        Gateway.SetupGet(value => value.IsConfigured).Returns(true);
        services.RemoveAll<IStripeGateway>(); services.AddSingleton(Gateway.Object);
        services.RemoveAll<ICustomerTablePaymentProofs>();
        services.AddScoped<ICustomerTablePaymentProofs>(provider => new ProofHook(
            ActivatorUtilities.CreateInstance<CustomerTablePaymentProofs>(provider), () =>
            { PreparedScopeObserved = true; AfterProof?.Invoke(); }));
    }

    protected async Task<CustomerTablePaymentIntentDto> PaymentIntentAsync(AccountPaymentMode mode = AccountPaymentMode.Full)
    {
        var round = await RoundIntentAsync();
        var reviewed = await PrepareRoundAsync(round);
        await RoundResolutionAsync(await SendAsync(RoundPath + "submit", Submission(round, reviewed)));
        await using var context = DatabaseFixture.CreateContext();
        var revision = await context.TableServiceSessions.Where(value => value.Id == Visit.ServiceSessionId)
            .Select(value => value.AccountRevision).SingleAsync();
        return new()
        {
            SchemaVersion = 1,
            TenantId = NativeTenantId,
            OperationId = Guid.NewGuid(),
            ServiceSessionId = Visit.ServiceSessionId,
            ExpectedAccountRevision = revision,
            Mode = mode
        };
    }

    protected async Task<CustomerTablePaymentPreparedDto> PreparedPaymentAsync(CustomerTablePaymentIntentDto request)
    {
        var response = await SendAsync(PreparationPath, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerTablePaymentPreparedDto>>(JsonOptions))!.Data!;
    }

    protected static async Task<CustomerTablePaymentResolutionDto> PaymentResolutionAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerTablePaymentResolutionDto>>(JsonOptions))!.Data!;
    }

    protected async Task NoPaymentEffectsAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(0);
        (await context.AccountCheckoutJournals.CountAsync()).Should().Be(0);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(1, "only the seeded round was accepted");
        Gateway.Invocations.Should().NotContain(call => call.Method.Name != "get_IsConfigured");
    }

    private sealed class ProofHook(ICustomerTablePaymentProofs inner, Action after) : ICustomerTablePaymentProofs
    {
        public CustomerTablePaymentProofDto Create(CustomerTablePaymentIntentDto request, CustomerTablePaymentOwner owner,
            CustomerTablePaymentBinding binding)
        { var proof = inner.Create(request, owner, binding); after(); return proof; }
        public CustomerTablePaymentBinding Require(CustomerTablePaymentProofDto proof, CustomerTablePaymentOwner owner) => inner.Require(proof, owner);
        public void RequireInput(CustomerTablePaymentIntentDto request, CustomerTablePaymentOwner owner,
            CustomerTablePaymentBinding binding, CustomerTablePaymentProofDto proof) => inner.RequireInput(request, owner, binding, proof);
    }
}
