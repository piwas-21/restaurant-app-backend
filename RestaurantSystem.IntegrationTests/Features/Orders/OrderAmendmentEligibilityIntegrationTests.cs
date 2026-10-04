using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class OrderAmendmentEligibilityIntegrationTests(DatabaseFixture fixture)
    : IntegrationTestBase(fixture)
{
    private readonly Guid _nativeOrderId = Guid.NewGuid();
    private readonly Guid _marketplaceOrderId = Guid.NewGuid();
    private readonly Guid _pendingOrderId = Guid.NewGuid();
    private readonly Guid _unprovenRefundOrderId = Guid.NewGuid();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantFeatures>();
        services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(
            new TenantFeatureSettings { OrderAmendmentsV1 = true })));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var now = DateTime.UtcNow;
        var unprovenRefund = NewTender(_unprovenRefundOrderId);
        unprovenRefund.Status = PaymentStatus.PartiallyRefunded;
        unprovenRefund.RefundedAmount = 1m;
        unprovenRefund.RefundDate = now;
        context.Orders.AddRange(NewOrder(_nativeOrderId, "EL-NATIVE"),
            NewOrder(_marketplaceOrderId, "EL-MARKET"), NewOrder(_pendingOrderId, "EL-PENDING"),
            NewOrder(_unprovenRefundOrderId, "EL-REFUND"));
        context.ExternalOrderReferences.Add(new ExternalOrderReference
        {
            OrderId = _marketplaceOrderId,
            Provider = "Marketplace",
            ExternalStoreId = "store-1",
            ExternalOrderId = "external-1",
            ExternalDisplayId = "display-1",
            ExternalState = "Accepted",
            LastEventAt = now,
            Currency = "CHF",
            MerchantTotal = 10m,
            PayloadHash = new string('c', 64),
            FulfillmentType = "Takeaway",
            CreatedBy = nameof(OrderAmendmentEligibilityIntegrationTests)
        });
        context.OrderPayments.AddRange(NewTender(_nativeOrderId), unprovenRefund);
        context.OrderAmendments.Add(NewPendingAmendment(_pendingOrderId, now));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Eligibility_distinguishes_native_supplement_only_and_unresolved_financial_states()
    {
        AuthenticateAsAdmin();
        var native = await ReadEligibilityAsync(_nativeOrderId, Client);
        native.CanCreateAmendment.Should().BeTrue();
        native.AmendmentMode.Should().Be("Native");

        var marketplace = await ReadEligibilityAsync(_marketplaceOrderId, Client);
        marketplace.CanCreateAmendment.Should().BeTrue();
        marketplace.AmendmentMode.Should().Be("LocalSupplementOnly");

        var pending = await ReadEligibilityAsync(_pendingOrderId, Client);
        pending.CanCreateAmendment.Should().BeFalse();
        pending.ReasonCode.Should().Be("financialResolutionPending");
        pending.AmendmentMode.Should().Be("None");

        var unproven = await ReadEligibilityAsync(_unprovenRefundOrderId, Client);
        unproven.CanCreateAmendment.Should().BeFalse();
        unproven.ReasonCode.Should().Be("refundReconciliationRequired");
        unproven.AmendmentMode.Should().Be("None");

        using var disabledFactory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantFeatures>();
            services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(new TenantFeatureSettings())));
        }));
        using var disabledClient = disabledFactory.CreateClient();
        disabledClient.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        var disabled = await ReadEligibilityAsync(_nativeOrderId, disabledClient);
        disabled.CanCreateAmendment.Should().BeFalse();
        disabled.ReasonCode.Should().Be("featureDisabled");
        disabled.AmendmentMode.Should().Be("None");
    }

    private static async Task<OrderAmendmentEligibilityDto> ReadEligibilityAsync(Guid orderId, HttpClient client)
    {
        using var response = await client.GetAsync($"/api/staff/orders/{orderId}/amendments/eligibility");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentEligibilityDto>>(JsonOptions))!.Data!;
    }

    private static Order NewOrder(Guid orderId, string prefix)
    {
        var now = DateTime.UtcNow;
        return new Order
        {
            Id = orderId,
            OrderNumber = $"{prefix}-{orderId:N}"[..18],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            Version = 1,
            SubTotal = 10m,
            Total = 10m,
            RemainingAmount = 10m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentEligibilityIntegrationTests),
            Items = [new OrderItem
            {
                Id = Guid.NewGuid(),
                ProductName = "Eligibility item",
                Quantity = 1,
                UnitPrice = 10m,
                ItemTotal = 10m,
                CreatedBy = nameof(OrderAmendmentEligibilityIntegrationTests)
            }]
        };
    }

    private static OrderPayment NewTender(Guid orderId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        PaymentMethod = PaymentMethod.Cash,
        Amount = 10m,
        Currency = "CHF",
        Status = PaymentStatus.Pending,
        PaymentDate = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(OrderAmendmentEligibilityIntegrationTests)
    };

    private static OrderAmendment NewPendingAmendment(Guid orderId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        SourceOrderId = orderId,
        ActorUserId = Guid.NewGuid(),
        ActorRole = UserRole.Cashier.ToString(),
        State = OrderAmendmentState.Committed,
        PayloadHash = new string('a', 64),
        CommitPayloadHash = new string('b', 64),
        ExpectedOrderVersion = 1,
        ExpiresAt = now.AddMinutes(5),
        CommittedAt = now,
        RequestJson = "{}",
        ChangesJson = "[]",
        SourceSnapshotJson = "{}",
        FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto(
            "CHF", 0, 1000, -1000, 1000,
            OrderAmendmentFinancialResolutionStatus.Pending,
            OrderAmendmentCreditState.PendingAllocationReview,
            OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.PendingTillRefund)),
        CreatedAt = now,
        CreatedBy = nameof(OrderAmendmentEligibilityIntegrationTests)
    };
}
