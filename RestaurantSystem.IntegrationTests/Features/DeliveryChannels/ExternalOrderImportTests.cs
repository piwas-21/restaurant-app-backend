using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ExternalOrderImportTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    [Fact]
    public async Task Import_PreservesProviderPricesNotesAndUnknownTax_WithoutLocalDiscountOrKitchenRelease()
    {
        var request = await PrepareAsync();
        var result = await ImportAsync(request);
        result.AlreadyImported.Should().BeFalse();
        result.OrderNumber.Should().NotBeNullOrWhiteSpace();
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(order => order.Items).Include(order => order.Payments)
            .SingleAsync(order => order.Id == result.OrderId);
        order.Status.Should().Be(OrderStatus.PendingApproval);
        order.IsKitchenReleased.Should().BeFalse();
        order.Total.Should().Be(5);
        order.TotalPaid.Should().Be(5);
        order.RemainingAmount.Should().Be(0);
        order.Items.Should().ContainSingle().Which.UnitPrice.Should().Be(5,
            "the tenant catalogue price is 12.99 or 2.99; this price is the provider's snapshot");
        order.Items.Single().ProductName.Should().Be("Marketplace meal");
        order.Items.Single().SpecialInstructions.Should().Be("No peanuts — allergy instruction fixture.");
        order.Notes.Should().Be("No food or courier.\nVerify the cart instructions.");
        order.OrderDate.Should().Be(new DateTime(2026, 10, 1, 17, 25, 7, DateTimeKind.Utc));
        order.UserId.Should().BeNull();
        order.CustomerEmail.Should().BeNull();
        order.QuickActionToken.Should().BeNull();
        order.Discount.Should().Be(0);
        order.FidelityPointsEarned.Should().Be(0);
        order.RoutingStates.Should().BeEmpty();
        order.ExternalReference!.ReportedTax.Should().BeNull();
        order.ExternalReference.IsSandbox.Should().BeTrue();
        var payment = order.Payments.Should().ContainSingle().Subject;
        payment.Amount.Should().Be(5);
        payment.Currency.Should().Be("CHF");
        payment.PaymentGateway.Should().Be("uber-eats");
        payment.TransactionId.Should().Be("provider-order-1");
        payment.Status.Should().Be(PaymentStatus.Completed);
        (await context.FidelityPointsTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentIdenticalImports_CreateExactlyOneOrderItemAndTender()
    {
        var request = await PrepareAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ImportAsync(request)));
        results.Select(result => result.OrderId).Distinct().Should().ContainSingle();
        results.Count(result => !result.AlreadyImported).Should().Be(1);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.OrderItems.CountAsync()).Should().Be(1);
        (await context.OrderPayments.CountAsync()).Should().Be(1);
        (await context.ExternalOrderReferences.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ChangedNormalizedContent_CannotReuseProviderHashOrOverwriteImportedOrder()
    {
        var request = await PrepareAsync();
        var original = await ImportAsync(request);
        var changed = request with { Instructions = "Changed allergy instructions" };
        var response = await PostAsJsonAsync(Endpoint, changed);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var context = DatabaseFixture.CreateContext();
        var persisted = await context.Orders.SingleAsync(order => order.Id == original.OrderId);
        persisted.Notes.Should().Be(request.Instructions);
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.OrderPayments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ExactReplay_RemainsValidAfterCatalogueAvailabilityChanges()
    {
        var request = await PrepareAsync();
        var original = await ImportAsync(request);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var product = await context.Products.SingleAsync(product => product.Id == request.Items[0].ProductId);
            product.IsAvailable = false;
            await context.SaveChangesAsync();
        }
        var replay = await ImportAsync(request);
        replay.OrderId.Should().Be(original.OrderId);
        replay.AlreadyImported.Should().BeTrue();
    }

    [Fact]
    public async Task MissingSecondMapping_DoesNotPersistPartialOrderTenderOrSource()
    {
        var request = await PrepareAsync();
        var invalid = request with { MerchantTotal = 10, Items = [request.Items[0], request.Items[0] with { ProductId = Guid.NewGuid() }] };
        var response = await PostAsJsonAsync(Endpoint, invalid);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        (await context.ExternalOrderReferences.CountAsync()).Should().Be(0);
        (await ImportAsync(request)).AlreadyImported.Should().BeFalse();
    }

    [Fact]
    public async Task RemovedOrder_CannotBeRecreatedByReplayingProviderIdentity()
    {
        var request = await PrepareAsync();
        var imported = await ImportAsync(request);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Orders.Remove(await context.Orders.SingleAsync(order => order.Id == imported.OrderId));
            await context.SaveChangesAsync();
        }
        var response = await PostAsJsonAsync(Endpoint, request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var readback = DatabaseFixture.CreateContext();
        (await readback.Orders.CountAsync()).Should().Be(0);
        (await readback.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await readback.ExternalOrderReferences.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("unavailable")]
    [InlineData("component")]
    [InlineData("channel")]
    [InlineData("bundle")]
    [InlineData("required-sauce")]
    [InlineData("foreign-variation")]
    public async Task UnsupportedProductMapping_IsRefusedWithoutDroppingItsChoices(string fault)
    {
        var request = await PrepareAsync();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var product = await context.Products.SingleAsync(product => product.Id == request.Items[0].ProductId);
            switch (fault)
            {
                case "inactive": product.IsActive = false; break;
                case "unavailable": product.IsAvailable = false; break;
                case "component": product.IsComponent = true; break;
                case "channel": product.AvailableOrderTypes = (int)OrderChannels.Takeaway; break;
                case "bundle": product.Type = ProductType.Menu; break;
                case "required-sauce": product.SauceMin = 1; break;
                case "foreign-variation": request = request with { Items = [request.Items[0] with { VariationId = Guid.NewGuid() }] }; break;
            }
            await context.SaveChangesAsync();
        }
        var response = await PostAsJsonAsync(Endpoint, request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var readback = DatabaseFixture.CreateContext();
        (await readback.Orders.CountAsync()).Should().Be(0);
        (await readback.ExternalOrderReferences.CountAsync()).Should().Be(0);
    }
}
