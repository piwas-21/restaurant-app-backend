using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

[Collection("Database Lane 2")]
public sealed class MenuOfferLinkRulesTests : IntegrationTestBase
{
    private Guid _anchorId;
    private Guid _anchorVariationId;
    private Guid _secondAnchorVariationId;
    private Guid _genericChildId;
    private Guid _variationChildId;
    private Guid _movingChildId;
    private Guid _replacementAnchorId;
    private Guid _cycleAId;
    private Guid _cycleBId;
    private Guid _raceChildId;

    public MenuOfferLinkRulesTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task Standalone_menu_anchor_allows_generic_and_variation_children()
    {
        AuthenticateAsAdmin();

        var generic = await LinkAsync(_genericChildId, _anchorId);
        var variation = await LinkAsync(_variationChildId, _anchorId, _anchorVariationId);

        generic.StatusCode.Should().Be(HttpStatusCode.OK);
        variation.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = DatabaseFixture.CreateContext();
        var links = await context.MenuDefinitions
            .Where(definition => definition.ParentOfferProductId == _anchorId)
            .Select(definition => definition.ParentOfferVariationId)
            .ToListAsync();
        links.Should().BeEquivalentTo(new Guid?[] { null, _anchorVariationId });
    }

    [Fact]
    public async Task Existing_child_can_be_reassigned_to_another_standalone_anchor()
    {
        AuthenticateAsAdmin();

        var response = await LinkAsync(_movingChildId, _anchorId, _secondAnchorVariationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MenuOfferLinkDto>>(JsonOptions);
        body!.Data!.ParentOfferProductId.Should().Be(_anchorId);
        await using var context = DatabaseFixture.CreateContext();
        (await context.MenuDefinitions.SingleAsync(definition => definition.ProductId == _movingChildId))
            .ParentOfferProductId.Should().Be(_anchorId);
    }

    [Fact]
    public async Task Variation_referenced_by_a_menu_section_cannot_be_archived()
    {
        await using var context = DatabaseFixture.CreateContext();

        Func<Task> action = () => MenuOfferLinkRules.EnsureCanDeactivateVariationAsync(
            context, _secondAnchorVariationId, false, CancellationToken.None);

        await action.Should().ThrowAsync<BadRequestException>()
            .WithMessage("*menu references*");
    }

    [Fact]
    public async Task Concurrent_cross_links_cannot_create_a_parent_cycle()
    {
        AuthenticateAsAdmin();

        var responses = await Task.WhenAll(
            LinkAsync(_cycleAId, _cycleBId),
            LinkAsync(_cycleBId, _cycleAId));

        responses.Count(response => response.IsSuccessStatusCode).Should().Be(1,
            "exactly one concurrent relationship write must win");
        var loser = responses.Single(response => !response.IsSuccessStatusCode);
        loser.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        var loserBody = await loser.Content.ReadAsStringAsync();
        (loserBody.Contains(MenuOfferLinkConflict.DuplicateMessage)
            || loserBody.Contains(MenuOfferLinkConflict.ConcurrentMessage)
            || loserBody.Contains("A linked menu cannot be used as another offer's parent")
            || loserBody.Contains("A menu with alternatives cannot be linked upward"))
            .Should().BeTrue("the loser must receive the relationship conflict reason");
        foreach (var response in responses)
        {
            response.Dispose();
        }

        await using var context = DatabaseFixture.CreateContext();
        var links = await context.MenuDefinitions
            .Where(definition => definition.ProductId == _cycleAId || definition.ProductId == _cycleBId)
            .ToDictionaryAsync(definition => definition.ProductId, definition => definition.ParentOfferProductId);
        (links[_cycleAId] == _cycleBId && links[_cycleBId] == _cycleAId)
            .Should().BeFalse("serializable link writes must not create a two-node parent cycle");
    }

    [Fact]
    public async Task Concurrent_link_and_delete_cannot_leave_a_deleted_child_linked()
    {
        AuthenticateAsAdmin();

        var linkTask = LinkAsync(_raceChildId, _anchorId);
        var deleteTask = Client.DeleteAsync($"/api/Menus/{_raceChildId}");
        var responses = await Task.WhenAll(linkTask, deleteTask);

        responses.Count(response => response.IsSuccessStatusCode).Should().Be(1);
        var loser = responses.Single(response => !response.IsSuccessStatusCode);
        loser.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound,
            HttpStatusCode.Conflict);

        foreach (var response in responses)
        {
            response.Dispose();
        }

        await using var context = DatabaseFixture.CreateContext();
        var child = await context.Products
            .IgnoreQueryFilters()
            .Include(product => product.MenuDefinition)
            .SingleAsync(product => product.Id == _raceChildId);

        if (child.IsDeleted)
        {
            child.MenuDefinition!.ParentOfferProductId.Should().BeNull();
        }
        else
        {
            child.MenuDefinition!.ParentOfferProductId.Should().Be(_anchorId);
        }
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var anchor = Menu("Anchor menu");
        var variation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Name = "Large",
            IsActive = true,
            CreatedBy = "test"
        };
        anchor.Variations.Add(variation);
        _anchorId = anchor.Id;
        _anchorVariationId = variation.Id;
        var secondVariation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            ProductId = anchor.Id,
            Name = "Small",
            IsActive = true,
            CreatedBy = "test"
        };
        anchor.Variations.Add(secondVariation);
        _secondAnchorVariationId = secondVariation.Id;
        var section = new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinition = anchor.MenuDefinition!,
            Name = "Choose size",
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedBy = "test"
        };
        section.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSection = section,
            Product = anchor,
            ProductId = anchor.Id,
            ProductVariation = secondVariation,
            ProductVariationId = secondVariation.Id,
            CreatedBy = "test"
        });
        anchor.MenuDefinition!.Sections.Add(section);

        var generic = Menu("Generic child");
        generic.MenuDefinition = Definition(generic, anchor.Id);
        _genericChildId = generic.Id;

        var variationChild = Menu("Variation child");
        variationChild.MenuDefinition = Definition(variationChild, anchor.Id, variation.Id);
        _variationChildId = variationChild.Id;

        var replacement = Menu("Replacement anchor");
        _replacementAnchorId = replacement.Id;
        var moving = Menu("Moving child");
        moving.MenuDefinition = Definition(moving, _replacementAnchorId);
        _movingChildId = moving.Id;
        var cycleA = Menu("Cycle A");
        var cycleB = Menu("Cycle B");
        _cycleAId = cycleA.Id;
        _cycleBId = cycleB.Id;
        var raceChild = Menu("Race child");
        _raceChildId = raceChild.Id;

        context.Products.AddRange(anchor, generic, variationChild, moving, replacement, cycleA, cycleB, raceChild);
        await context.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> LinkAsync(Guid menuId, Guid parentId, Guid? variationId = null) =>
        Client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/Menus/{menuId}/offer-parent")
        {
            Content = JsonContent.Create(new
            {
                parentOfferProductId = parentId,
                parentOfferVariationId = variationId
            }, options: JsonOptions)
        });

    private static Product Menu(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = 10m,
        Type = ProductType.Menu,
        IsActive = true,
        IsAvailable = true,
        Ingredients = [],
        Allergens = [],
        CreatedBy = "test",
        MenuDefinition = new MenuDefinition { IsAlwaysAvailable = true, CreatedBy = "test" }
    };

    private static MenuDefinition Definition(Product menu, Guid parentId, Guid? variationId = null) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = menu.Id,
        ParentOfferProductId = parentId,
        ParentOfferVariationId = variationId,
        Product = menu,
        IsAlwaysAvailable = true,
        CreatedBy = "test"
    };
}
