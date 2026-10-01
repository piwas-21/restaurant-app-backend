using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ExternalOrderSnapshotBoundaryTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    [Theory]
    [InlineData(50)]
    [InlineData(51)]
    public async Task VariationSnapshot_UsesPersistedNameLimit_WithoutRepricing(int length)
    {
        var request = await PrepareAsync();
        var variationId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Set<ProductVariation>().Add(new()
            {
                Id = variationId,
                ProductId = request.Items[0].ProductId,
                Name = "Tenant variation",
                IsActive = true,
                PriceModifier = 20,
                CreatedBy = "test",
            });
            await context.SaveChangesAsync();
        }
        request = request with { Items = [request.Items[0] with { VariationId = variationId, VariationName = new string('V', length) }] };
        var response = await PostAsJsonAsync(Endpoint, request);
        response.StatusCode.Should().Be(length == 50 ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        await using var readback = DatabaseFixture.CreateContext();
        if (length == 50)
        {
            var item = await readback.OrderItems.SingleAsync();
            item.VariationName.Should().Be(new string('V', 50));
            item.UnitPrice.Should().Be(5);
        }
        else
            (await readback.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExplicitZeroPrice_IsSupported_WhileAbsentMoneyIsRejected()
    {
        var request = await PrepareAsync();
        request = request with { MerchantTotal = 0, ReportedTax = 0, Items = [request.Items[0] with { UnitPrice = 0, Total = 0 }] };
        var imported = await ImportAsync(request);
        await using var readback = DatabaseFixture.CreateContext();
        var order = await readback.Orders.Include(order => order.Payments).SingleAsync(order => order.Id == imported.OrderId);
        order.Total.Should().Be(0);
        order.ExternalReference!.ReportedTax.Should().Be(0);
        order.Payments.Should().ContainSingle().Which.Amount.Should().Be(0);
    }
}
