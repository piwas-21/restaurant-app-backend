using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

[Collection("Database Lane 3")]
public sealed class MenuOfferRelationshipLifecycleTests : IntegrationTestBase
{
    private Guid _categoryId;
    private Guid _parentBundleId;
    private Guid _linkedMenuId;

    public MenuOfferRelationshipLifecycleTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task Updating_a_bundle_parent_to_inactive_is_rejected_when_it_has_alternatives()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Menus/{_parentBundleId}",
            new
            {
                id = _parentBundleId,
                name = "Parent bundle",
                basePrice = 20m,
                isActive = false,
                isAvailable = true,
                isSpecial = false,
                preparationTimeMinutes = 10,
                displayOrder = 0,
                categoryIds = Array.Empty<Guid>(),
                menuDefinition = new { isAlwaysAvailable = true, sections = Array.Empty<object>() },
                content = new { }
            },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("alternatives");
    }

    [Fact]
    public async Task A_linked_menu_cannot_change_type_while_its_parent_relation_exists()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Products/{_linkedMenuId}",
            ProductUpdatePayload(ProductType.MainItem, isComponent: false),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unlink");
    }

    [Fact]
    public async Task A_linked_menu_cannot_become_a_component()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Products/{_linkedMenuId}",
            ProductUpdatePayload(ProductType.Menu, isComponent: true),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("alternative");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _categoryId = await context.Categories.Select(category => category.Id).FirstAsync();

        var parent = Menu("Parent bundle");
        _parentBundleId = parent.Id;
        var linkedMenu = Menu("Linked menu");
        _linkedMenuId = linkedMenu.Id;
        linkedMenu.MenuDefinition = new MenuDefinition
        {
            ProductId = linkedMenu.Id,
            ParentOfferProductId = parent.Id,
            IsAlwaysAvailable = true,
            CreatedBy = "test"
        };

        context.Products.AddRange(parent, linkedMenu);
        await context.SaveChangesAsync();
    }

    private object ProductUpdatePayload(ProductType type, bool isComponent) => new
    {
        id = _linkedMenuId,
        name = "Linked menu",
        basePrice = 12m,
        isActive = true,
        isAvailable = true,
        isSpecial = false,
        preparationTimeMinutes = 10,
        type = (int)type,
        kitchenType = (int)KitchenType.None,
        displayOrder = 0,
        categoryIds = new[] { _categoryId },
        primaryCategoryId = _categoryId,
        isComponent,
        menuDefinition = type == ProductType.Menu
            ? new { isAlwaysAvailable = true, sections = Array.Empty<object>() }
            : null
    };

    private static Product Menu(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = 20m,
        Type = ProductType.Menu,
        IsActive = true,
        IsAvailable = true,
        Ingredients = [],
        Allergens = [],
        CreatedBy = "test",
        MenuDefinition = new MenuDefinition { IsAlwaysAvailable = true, CreatedBy = "test" }
    };
}
