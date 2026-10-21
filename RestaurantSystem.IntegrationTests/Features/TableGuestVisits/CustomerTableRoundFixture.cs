using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Features.Orders;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public abstract class CustomerTableRoundFixture(DatabaseFixture fixture) : CustomerCheckoutAcceptanceFixture(fixture)
{
    protected const string RoundPath = "/api/customer-mobile/table-rounds/";
    protected Guid NativeTenantId { get; } = Guid.NewGuid();
    protected Guid TableId { get; } = Guid.NewGuid();
    protected TableGuestJoinDto Visit { get; private set; } = new(Guid.Empty, string.Empty, default);
    protected readonly Mock<ITenantFeatures> Features = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<CustomerMobileSettings>(settings =>
        {
            settings.TenantId = NativeTenantId;
            settings.EnabledCapabilities = ["ordering", "accounts", "table-visits"];
        });
        Features.SetupGet(value => value.TableGuestVisitsV1).Returns(true);
        Features.SetupGet(value => value.TableVisitReadinessV1).Returns(true);
        services.RemoveAll<ITenantFeatures>(); services.AddSingleton(Features.Object);
        var modules = new Mock<ITenantModules>();
        modules.SetupGet(value => value.IsEnforced).Returns(true);
        modules.SetupGet(value => value.EnabledModules).Returns([ModuleIds.Core, ModuleIds.Server]);
        modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns((string id) => id is ModuleIds.Core or ModuleIds.Server);
        services.RemoveAll<ITenantModules>(); services.AddSingleton(modules.Object);
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
    }
    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        var qr = $"native-table-round-{Guid.NewGuid():N}";
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Tables.Add(new Table
            {
                Id = TableId,
                TableNumber = "Garden",
                MaxGuests = 4,
                IsActive = true,
                ReadinessState = TableReadinessState.ReadyForGuests,
                QRCodeData = qr,
                QRCodeGeneratedAt = DateTime.UtcNow,
                CreatedBy = nameof(CustomerTableRoundFixture)
            });
            var configuration = await context.OrderTypeConfigurations.SingleOrDefaultAsync(value => value.OrderType == OrderType.DineIn);
            if (configuration is null)
            {
                configuration = new() { OrderType = OrderType.DineIn, CreatedBy = nameof(CustomerTableRoundFixture) };
                context.OrderTypeConfigurations.Add(configuration);
            }
            configuration.IsEnabled = true; configuration.EnforceOpeningHours = false;
            await context.SaveChangesAsync();
        }
        var response = await PostAsJsonAsync("/api/table-guest-visits/join", new { qrCodeData = qr });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Visit = (await response.Content.ReadFromJsonAsync<ApiResponse<TableGuestJoinDto>>(JsonOptions))!.Data!;
        Client.DefaultRequestHeaders.Add("X-Table-Participant", Visit.ParticipantToken);
    }
    protected async Task<CustomerTableRoundIntentDto> RoundIntentAsync()
    {
        var basket = await CheckoutBasketAsync(OrderType.DineIn);
        await using var context = DatabaseFixture.CreateContext();
        var revision = await context.TableServiceSessions.Where(value => value.Id == Visit.ServiceSessionId)
            .Select(value => value.AccountRevision).SingleAsync();
        return new()
        {
            SchemaVersion = 1,
            OperationId = Guid.NewGuid(),
            TenantId = NativeTenantId,
            ServiceSessionId = Visit.ServiceSessionId,
            ExpectedAccountRevision = revision,
            ExpectedBasketId = basket.Id,
            ExpectedPurchaseFingerprint = basket.PurchaseFingerprint
        };
    }
    protected async Task<CustomerTableRoundPreparedDto> PrepareRoundAsync(CustomerTableRoundIntentDto intent)
    {
        var response = await SendAsync(RoundPath + "prepare", intent);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerTableRoundPreparedDto>>(JsonOptions))!.Data!;
    }
    protected static CustomerTableRoundIntentDto Submission(CustomerTableRoundIntentDto intent, CustomerTableRoundPreparedDto prepared) =>
        intent with { PayloadHash = prepared.Proof.PayloadHash, OwnerProof = prepared.Proof.OwnerProof };
    protected Task<HttpResponseMessage> ResolveRoundAsync(CustomerTableRoundPreparedDto prepared, bool stop = false, string? session = null) =>
        SendAsync(RoundPath + (stop ? "cancel-attempt" : "recover"), prepared.Proof, session);
    protected static async Task<CustomerTableRoundResolutionDto> RoundResolutionAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain("ownerProof").And.NotContain("participantToken").And.NotContain("customerEmail")
            .And.NotContain("quickActionToken").And.NotContain("guestStatusToken");
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerTableRoundResolutionDto>>(JsonOptions))!.Data!;
    }
    protected void WithdrawNative() => Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value.EnabledCapabilities = [];
    protected async Task<Guid> SignInCustomerAsync()
    {
        var id = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        context.Users.Add(new ApplicationUser
        {
            Id = id,
            FirstName = "Round",
            LastName = "Customer",
            Role = UserRole.Customer,
            UserName = $"round-{id:N}",
            Email = $"round-{id:N}@example.test",
            EmailConfirmed = true,
            RefreshToken = string.Empty,
            CreatedBy = nameof(CustomerTableRoundFixture)
        });
        await context.SaveChangesAsync();
        UseCustomer(id);
        return id;
    }
    protected void UseCustomer(Guid id) => Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
        CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, userId: id.ToString("D")));
}
