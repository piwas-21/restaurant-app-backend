using FluentAssertions;
using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

public class MenuBundleAvailabilityResolverTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Resolve_RefusesInactiveOrDeletedRootBundles(bool isActive, bool isDeleted)
    {
        var product = new Product
        {
            Name = "Unavailable root bundle",
            Type = ProductType.Menu,
            IsActive = isActive,
            IsAvailable = true,
            IsDeleted = isDeleted,
            CreatedBy = "test",
            MenuDefinition = new MenuDefinition { IsAlwaysAvailable = true, CreatedBy = "test" }
        };

        var availability = MenuBundleAvailabilityResolver.Resolve(product, OrderType.DineIn);

        availability.Should().BeEquivalentTo(new ItemAvailabilityDto
        {
            CanOrder = false,
            Reason = AvailabilityReason.Unavailable,
            AllowedOrderTypes = [],
            InheritsOrderTypes = true
        });
    }
}
