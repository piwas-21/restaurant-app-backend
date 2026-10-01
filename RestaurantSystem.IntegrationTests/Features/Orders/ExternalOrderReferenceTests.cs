using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class ExternalOrderReferenceTests(DatabaseFixture databaseFixture) : IntegrationTestBase(databaseFixture)
{
    [Fact]
    public async Task StaffAndPrinterReads_PreserveMarketplaceMoneyIdentityAndUnknownTax()
    {
        var orderId = await SaveOrderAsync();
        AuthenticateAsRole(UserRole.KitchenStaff);
        var staff = await Client.GetAsync($"/api/orders/{orderId}");
        staff.EnsureSuccessStatusCode();
        using var staffJson = JsonDocument.Parse(await staff.Content.ReadAsStringAsync());
        staffJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        AssertSource(staffJson.RootElement.GetProperty("data"));

        AuthenticateAsDevice();
        var feed = await Client.GetAsync("/api/orders/printer-feed");
        feed.EnsureSuccessStatusCode();
        using var feedJson = JsonDocument.Parse(await feed.Content.ReadAsStringAsync());
        feedJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var printed = feedJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == orderId);
        AssertSource(printed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task ExplicitAsyncMapping_LoadsReferenceAndDistinguishesMissingFromZeroTax(int? reportedTax)
    {
        var orderId = await SaveOrderAsync(reportedTax);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await context.Orders.IgnoreAutoIncludes().SingleAsync(order => order.Id == orderId);
        context.Entry(order).Reference(order => order.ExternalReference).IsLoaded.Should().BeFalse();
        var dto = await scope.ServiceProvider.GetRequiredService<IOrderMappingService>().MapToOrderDtoAsync(order);
        dto.ExternalOrder!.ReportedTax.Should().Be(reportedTax);
        dto.Currency.Should().Be("EUR", "the isolated test tenant has CHF, but this order was priced in EUR");
        var json = JsonSerializer.SerializeToElement(dto, JsonOptions);
        json.GetProperty("externalOrder").GetProperty("reportedTax").ValueKind.Should()
            .Be(reportedTax is null ? JsonValueKind.Null : JsonValueKind.Number);
        json.GetProperty("externalOrder").TryGetProperty("payloadHash", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DatabaseRejects_DuplicateLocalLinkOrProviderStoreOrder(bool sameLocalOrder)
    {
        var orderId = await SaveOrderAsync();
        await using var context = DatabaseFixture.CreateContext();
        var targetId = sameLocalOrder ? orderId : Guid.NewGuid();
        if (!sameLocalOrder)
        {
            context.Orders.Add(NewOrder(targetId));
            await context.SaveChangesAsync();
        }
        var duplicate = NewReference(targetId);
        if (sameLocalOrder)
        {
            duplicate.ExternalOrderId = "different-provider-order";
        }
        // Bypass EF relationship fixup: this assertion is about the persisted unique keys,
        // independently of the ORM's one-to-one navigation collision detection.
        var failure = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ExternalOrderReferences"
                (id, order_id, provider, external_store_id, external_order_id, external_display_id,
                 external_state, last_event_at, currency, merchant_total, payload_hash, fulfillment_type, is_sandbox, created_by)
            VALUES ({duplicate.Id}, {duplicate.OrderId}, {duplicate.Provider}, {duplicate.ExternalStoreId},
                {duplicate.ExternalOrderId}, {duplicate.ExternalDisplayId}, {duplicate.ExternalState}, {duplicate.LastEventAt},
                {duplicate.Currency}, {duplicate.MerchantTotal}, {duplicate.PayloadHash}, {duplicate.FulfillmentType},
                {duplicate.IsSandbox}, {duplicate.CreatedBy})
            """));
        failure.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("hash")]
    [InlineData("total")]
    [InlineData("tax")]
    public async Task DatabaseRejects_InvalidCurrencyHashOrMoney(string invalidField)
    {
        await using var context = DatabaseFixture.CreateContext();
        var order = NewOrder(Guid.NewGuid());
        var reference = NewReference(order.Id);
        switch (invalidField)
        {
            case "currency": reference.Currency = "eur"; break;
            case "hash": reference.PayloadHash = new string('x', 64); break;
            case "total": reference.MerchantTotal = -1; break;
            case "tax": reference.ReportedTax = -1; break;
        }
        order.ExternalReference = reference;
        context.Orders.Add(order);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        failure.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task ProviderIdentity_IncludesStoreAndSurvivesSoftDeletionWithoutKeyReuse()
    {
        var firstId = await SaveOrderAsync();
        await using var context = DatabaseFixture.CreateContext();
        var first = await context.Orders.SingleAsync(order => order.Id == firstId);
        context.Orders.Remove(first);
        var otherStoreOrder = NewOrder(Guid.NewGuid());
        otherStoreOrder.ExternalReference = NewReference(otherStoreOrder.Id);
        otherStoreOrder.ExternalReference.ExternalStoreId = "other-approved-store";
        context.Add(otherStoreOrder);
        await context.SaveChangesAsync();
        first.IsDeleted.Should().BeTrue();
        (await context.ExternalOrderReferences.CountAsync()).Should().Be(2,
            "soft deletion must not hide the durable external idempotency anchor");

        var reused = NewOrder(Guid.NewGuid());
        reused.ExternalReference = NewReference(reused.Id);
        context.Add(reused);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        failure.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    private async Task<Guid> SaveOrderAsync(int? reportedTax = null)
    {
        await using var context = DatabaseFixture.CreateContext();
        var order = NewOrder(Guid.NewGuid());
        order.ExternalReference = NewReference(order.Id);
        order.ExternalReference.ReportedTax = reportedTax;
        context.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task UpdatingOnlyExternalState_AdvancesOrderVersionAndDeltaFeedJournal()
    {
        var orderId = await SaveOrderAsync();
        await using var context = DatabaseFixture.CreateContext();
        var before = await context.Orders.AsNoTracking().SingleAsync(order => order.Id == orderId);
        var reference = await context.ExternalOrderReferences.SingleAsync(reference => reference.OrderId == orderId);
        reference.ExternalState = "CANCELED";
        reference.LastEventAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        await using var readback = DatabaseFixture.CreateContext();
        var after = await readback.Orders.AsNoTracking().SingleAsync(order => order.Id == orderId);
        after.Version.Should().Be(before.Version + 1);
        after.UpdatedAt.Should().NotBeNull();
        after.LastChangeSequence.Should().BeGreaterThan(before.LastChangeSequence);
    }

    private static Order NewOrder(Guid id) => new()
    {
        Id = id,
        OrderNumber = "EXT-" + id.ToString("N")[..12],
        Type = OrderType.Delivery,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Completed,
        IsKitchenReleased = true,
        SubTotal = 5,
        Total = 5,
        TotalPaid = 5,
        OrderDate = DateTime.UtcNow,
        CreatedBy = "test",
    };

    private static ExternalOrderReference NewReference(Guid orderId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Provider = "uber-eats",
        ExternalStoreId = "test-store",
        ExternalOrderId = "sandbox-order",
        ExternalDisplayId = "9116D",
        ExternalState = "ACCEPTED",
        LastEventAt = DateTime.UtcNow,
        Currency = "EUR",
        MerchantTotal = 5,
        PayloadHash = new string('a', 64),
        FulfillmentType = "DELIVERY_BY_UBER",
        IsSandbox = true,
        CreatedBy = "test",
    };

    private static void AssertSource(JsonElement dto)
    {
        dto.GetProperty("total").GetDecimal().Should().Be(5, "customer marketplace fees are not merchant revenue");
        dto.GetProperty("currency").GetString().Should().Be("EUR");
        var source = dto.GetProperty("externalOrder");
        source.GetProperty("provider").GetString().Should().Be("uber-eats");
        source.GetProperty("externalDisplayId").GetString().Should().Be("9116D");
        source.GetProperty("merchantTotal").GetDecimal().Should().Be(5);
        source.GetProperty("reportedTax").ValueKind.Should().Be(JsonValueKind.Null);
        source.GetProperty("isSandbox").GetBoolean().Should().BeTrue();
        source.TryGetProperty("payloadHash", out _).Should().BeFalse();
        source.TryGetProperty("externalOrderId", out _).Should().BeFalse();
        source.TryGetProperty("externalStoreId", out _).Should().BeFalse();
    }
}
