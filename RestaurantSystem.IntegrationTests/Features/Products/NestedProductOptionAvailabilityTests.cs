using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Products;

[Collection("Database Lane 2")]
public class NestedProductOptionAvailabilityTests : IntegrationTestBase
{
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);
    private Guid _parentProductId;
    private Guid _restrictedOptionId;
    private Guid _inactiveOptionId;
    private Guid _unavailableOptionId;
    private Guid _deletedOptionId;
    private Guid _restrictedMembershipId;

    public NestedProductOptionAvailabilityTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task ProductDetail_ResolvesNestedOptionsAgainstTheirCurrentStateAndInheritedChannel()
    {
        var dineIn = await ReadDetailAsync(OrderType.DineIn);
        var options = dineIn.CustomizationGroups.Single().ProductOptions;
        var restricted = options.Single(option => option.OptionProductId == _restrictedOptionId);
        var inactive = options.Single(option => option.OptionProductId == _inactiveOptionId);
        var unavailable = options.Single(option => option.OptionProductId == _unavailableOptionId);
        var deleted = options.Single(option => option.OptionProductId == _deletedOptionId);

        restricted.Id.Should().Be(_restrictedMembershipId);
        restricted.OptionProductIsActive.Should().BeTrue();
        restricted.OptionProductIsAvailable.Should().BeTrue();
        restricted.Availability.CanOrder.Should().BeFalse();
        restricted.Availability.Reason.Should().Be(AvailabilityReason.WrongOrderType);
        restricted.Availability.AllowedOrderTypes.Should().Equal(OrderType.Takeaway, OrderType.Delivery);

        inactive.OptionProductIsActive.Should().BeFalse();
        inactive.OptionProductIsAvailable.Should().BeTrue();
        inactive.Availability.CanOrder.Should().BeFalse();
        inactive.Availability.Reason.Should().Be(AvailabilityReason.Unavailable);

        unavailable.OptionProductIsActive.Should().BeTrue();
        unavailable.OptionProductIsAvailable.Should().BeFalse();
        unavailable.Availability.CanOrder.Should().BeFalse();
        unavailable.Availability.Reason.Should().Be(AvailabilityReason.Unavailable);

        deleted.OptionProductIsActive.Should().BeFalse();
        deleted.OptionProductIsAvailable.Should().BeFalse();
        deleted.Availability.CanOrder.Should().BeFalse();
        deleted.Availability.Reason.Should().Be(AvailabilityReason.Unavailable);

        var takeaway = await ReadDetailAsync(OrderType.Takeaway);
        var takeawayOption = takeaway.CustomizationGroups.Single().ProductOptions
            .Single(option => option.OptionProductId == _restrictedOptionId);
        takeawayOption.Availability.CanOrder.Should().BeTrue();
        takeawayOption.Availability.Reason.Should().Be(AvailabilityReason.Available);
    }

    private async Task<ProductDto> ReadDetailAsync(OrderType requestedOrderType)
    {
        var response = await Client.GetAsync(
            $"/api/Products/{_parentProductId}?requestedOrderType={requestedOrderType}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<ProductDto>>(JsonOptions);
        envelope!.Success.Should().BeTrue();
        return envelope.Data!;
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Nested option takeaway and delivery",
            AvailableOrderTypes = TakeawayAndDelivery,
            IsActive = true,
            CreatedBy = "test"
        };
        var parent = CreateProduct("Nested option parent");
        var restricted = CreateProduct("Restricted nested option");
        var inactive = CreateProduct("Inactive nested option");
        inactive.IsActive = false;
        var unavailable = CreateProduct("Unavailable nested option");
        unavailable.IsAvailable = false;
        var deleted = CreateProduct("Deleted nested option");
        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        restricted.ProductCategories.Add(new ProductCategory
        {
            Product = restricted,
            Category = category,
            IsPrimary = true,
            CreatedBy = "test"
        });

        var group = new ProductCustomizationGroup
        {
            Id = Guid.NewGuid(),
            Product = parent,
            Name = "Nested choices",
            MinSelection = 0,
            MaxSelection = 3,
            IsActive = true,
            CreatedBy = "test"
        };
        var restrictedMembership = AddOption(group, restricted);
        AddOption(group, inactive);
        AddOption(group, unavailable);
        AddOption(group, deleted);
        parent.CustomizationGroups.Add(group);

        context.AddRange(category, parent, restricted, inactive, unavailable, deleted);
        await context.SaveChangesAsync();

        _parentProductId = parent.Id;
        _restrictedOptionId = restricted.Id;
        _inactiveOptionId = inactive.Id;
        _unavailableOptionId = unavailable.Id;
        _deletedOptionId = deleted.Id;
        _restrictedMembershipId = restrictedMembership.Id;
    }

    private static Product CreateProduct(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = 1m,
        IsActive = true,
        IsAvailable = true,
        Type = ProductType.MainItem,
        Ingredients = [],
        Allergens = [],
        CreatedBy = "test"
    };

    private static ProductCustomizationProductOption AddOption(
        ProductCustomizationGroup group,
        Product product)
    {
        var option = new ProductCustomizationProductOption
        {
            Id = Guid.NewGuid(),
            ProductCustomizationGroup = group,
            OptionProduct = product,
            AdditionalPrice = 0m,
            DisplayOrder = group.ProductOptions.Count,
            CreatedBy = "test"
        };
        group.ProductOptions.Add(option);
        return option;
    }
}
