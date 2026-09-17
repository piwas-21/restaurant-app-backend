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
    private Guid _unlinkedMenuId;

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

    [Fact]
    public async Task A_parent_or_linked_child_cannot_be_soft_deleted_until_unlinked()
    {
        AuthenticateAsAdmin();

        var parentResponse = await Client.DeleteAsync($"/api/Products/{_parentBundleId}");
        parentResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await parentResponse.Content.ReadAsStringAsync()).Should().Contain("alternatives");

        var childResponse = await Client.DeleteAsync($"/api/Menus/{_linkedMenuId}");
        childResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await childResponse.Content.ReadAsStringAsync()).Should().Contain("alternative");

        await using var context = DatabaseFixture.CreateContext();
        (await context.Products
            .Where(product => product.Id == _parentBundleId || product.Id == _linkedMenuId)
            .Select(product => product.IsDeleted)
            .ToListAsync())
            .Should().OnlyContain(isDeleted => !isDeleted);
    }

    [Fact]
    public async Task UpdateProduct_rejects_component_state_when_assigning_a_parent()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Products/{_unlinkedMenuId}",
            ProductUpdatePayload(
                ProductType.Menu,
                isComponent: true,
                parentId: _parentBundleId,
                productId: _unlinkedMenuId),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("component");

        await using var context = DatabaseFixture.CreateContext();
        var menu = await context.Products
            .Include(product => product.MenuDefinition)
            .SingleAsync(product => product.Id == _unlinkedMenuId);
        menu.IsComponent.Should().BeFalse();
        menu.MenuDefinition!.ParentOfferProductId.Should().BeNull();
    }

    [Fact]
    public async Task Narrow_link_rejects_a_menu_that_is_already_a_component()
    {
        AuthenticateAsAdmin();

        await using (var context = DatabaseFixture.CreateContext())
        {
            var menu = await context.Products.SingleAsync(product => product.Id == _unlinkedMenuId);
            menu.IsComponent = true;
            await context.SaveChangesAsync();
        }

        var response = await Client.SendAsync(new HttpRequestMessage(
            HttpMethod.Patch, $"/api/Menus/{_unlinkedMenuId}/offer-parent")
        {
            Content = JsonContent.Create(
                new { parentOfferProductId = _parentBundleId }, options: JsonOptions)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("component");
    }

    [Fact]
    public async Task A_parent_with_alternatives_cannot_change_product_type()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Products/{_parentBundleId}",
            ProductUpdatePayload(ProductType.MainItem, isComponent: false, productId: _parentBundleId),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("relationship");
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

        var unlinkedMenu = Menu("Unlinked menu");
        _unlinkedMenuId = unlinkedMenu.Id;

        context.Products.AddRange(parent, linkedMenu, unlinkedMenu);
        await context.SaveChangesAsync();
    }

    private object ProductUpdatePayload(
        ProductType type,
        bool isComponent,
        Guid? parentId = null,
        Guid? productId = null) => new
        {
            id = productId ?? _linkedMenuId,
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
                ? new
                {
                    isAlwaysAvailable = true,
                    parentOfferProductId = parentId,
                    sections = Array.Empty<object>()
                }
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
