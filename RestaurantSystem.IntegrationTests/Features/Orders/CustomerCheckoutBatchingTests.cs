using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutBatchingTests(DatabaseFixture fixture) : CustomerCheckoutCompositionFixture(fixture)
{
    [Fact]
    public async Task Building_many_priced_bundle_roots_uses_the_loaded_graph_without_per_component_queries()
    {
        var basket = await PrepareAsync(new AddToBasketDto
        {
            OperationId = Guid.NewGuid(),
            ProductId = _bundleId,
            Quantity = 1,
            SelectedMenuOptions = [new()
            {
                SectionId = _sectionId, ItemId = ProductId, Quantity = 1, CustomizationSelections = Options(false)
            }]
        });
        var template = basket.Items[0];
        basket.Items = Enumerable.Range(0, 8).Select(index => template with
        {
            Id = Guid.NewGuid(),
            SpecialInstructions = $"Line {index}",
            ChildItems = template.ChildItems!.Select(child => child with { Id = Guid.NewGuid() }).ToList()
        }).ToList();
        var counter = new ReadCounter();
        await using var context = DatabaseFixture.CreateContext(counter);
        (await context.MenuSectionItems.CountAsync()).Should().Be(1);
        counter.Count.Should().Be(1, "the interceptor must observe a known database read");
        var pricing = new BasketPricingService(Mock.Of<ICustomerDiscountService>(),
            Microsoft.Extensions.Options.Options.Create(new OrderSettings()), NullLogger<BasketPricingService>.Instance);
        var customizations = new LineCustomizationBuilder(pricing);
        var batch = new StaffCounterOrderPricing(context, customizations);
        var translator = new BasketToOrderTranslator();
        var priced = await batch.PriceBasketAsync(basket, translator.Translate(basket.Items), CancellationToken.None);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(user => user.GetAuditIdentifier()).Returns("quote-batching");
        var factory = new OrderItemFactory(context, currentUser.Object, customizations, Mock.Of<ITenantFeatures>());
        var order = new Order { CreatedBy = "quote-batching" };

        counter.Count = 0;
        foreach (var line in priced)
            (await factory.AddItemAsync(order, line, true, CancellationToken.None, allowStaffPrices: false)).Should().BeNull();
        counter.Count.Should().Be(0, "the fully loaded graph must satisfy every product and exact section lookup");
        order.Items.Should().HaveCount(16);
        order.Items.Sum(item => item.ItemTotal).Should().Be(80m); // Eight (8 base + 2 section) roots.
        context.ChangeTracker.HasChanges().Should().BeFalse();
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Count { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
