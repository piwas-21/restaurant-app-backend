using System.Data.Common;
using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.OptionSets.Search;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace RestaurantSystem.IntegrationTests.Features.OptionSets;

[Collection("Database Lane 1")]
public sealed class MenuAuthoringSearchTests : IntegrationTestBase
{
    private const string Actor = "menu-authoring-search-test";
    private readonly ITestOutputHelper _output;

    public MenuAuthoringSearchTests(DatabaseFixture databaseFixture, ITestOutputHelper output)
        : base(databaseFixture)
    {
        _output = output;
    }

    [Fact]
    public async Task Search_persists_aliases_rejections_and_keyset_pages()
    {
        AuthenticateAsAdmin();
        var aliasProductId = Guid.NewGuid();
        var rejectedProductId = Guid.NewGuid();
        var cursorProductIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Products.AddRange(
                NewProduct(aliasProductId, "Sriracha sauce"),
                NewProduct(rejectedProductId, "Chili oil"),
                NewProduct(cursorProductIds[0], "Cursorpaged Alpha"),
                NewProduct(cursorProductIds[1], "Cursorpaged Bravo"),
                NewProduct(cursorProductIds[2], "Cursorpaged Charlie"),
                NewProduct(cursorProductIds[3], "Cursorpaged Delta"));
            await context.SaveChangesAsync();
        }

        var accepted = await PostAsJsonAsync("/api/MenuAuthoring/match-decisions", new
        {
            query = "harissa",
            candidateType = "product",
            candidateId = aliasProductId,
            decision = "accept",
            alias = "harissa"
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK,
            await accepted.Content.ReadAsStringAsync());

        var aliased = await GetFromJsonAsync<ApiResponse<MenuAuthoringSearchPageDto>>(
            "/api/MenuAuthoring/search?q=harissa&limit=24");
        aliased!.Data!.Items.Should().ContainSingle(candidate => candidate.Id == aliasProductId)
            .Which.MatchSource.Should().Be("alias");

        var rejected = await PostAsJsonAsync("/api/MenuAuthoring/match-decisions", new
        {
            query = "chili",
            candidateType = "product",
            candidateId = rejectedProductId,
            decision = "reject"
        });
        rejected.StatusCode.Should().Be(HttpStatusCode.OK,
            await rejected.Content.ReadAsStringAsync());
        var afterRejection = await GetFromJsonAsync<ApiResponse<MenuAuthoringSearchPageDto>>(
            "/api/MenuAuthoring/search?q=chili&limit=24");
        afterRejection!.Data!.Items.Should().NotContain(candidate => candidate.Id == rejectedProductId);

        var firstPage = await GetFromJsonAsync<ApiResponse<MenuAuthoringSearchPageDto>>(
            "/api/MenuAuthoring/search?q=cursorpaged&limit=2");
        firstPage!.Data!.Items.Should().HaveCount(2);
        firstPage.Data.NextCursor.Should().NotBeNullOrWhiteSpace();
        var nextCursor = Uri.EscapeDataString(firstPage.Data.NextCursor!);
        var secondPageResponse = await Client.GetAsync(
            $"/api/MenuAuthoring/search?q=cursorpaged&limit=2&cursor={nextCursor}");
        secondPageResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await secondPageResponse.Content.ReadAsStringAsync());
        var secondPage = await ReadResponseAsync<ApiResponse<MenuAuthoringSearchPageDto>>(secondPageResponse);
        secondPage!.Data!.Items.Should().HaveCount(2);
        secondPage.Data.Items.Select(candidate => candidate.Id)
            .Should().NotIntersectWith(firstPage.Data.Items.Select(candidate => candidate.Id));
        firstPage.Data.Items.Concat(secondPage.Data.Items).Select(candidate => candidate.Id)
            .Should().BeEquivalentTo(cursorProductIds);
    }

    [Fact]
    public async Task Search_stays_bounded_on_mc_food_sized_catalogue()
    {
        await SeedRepresentativeCatalogueAsync();
        var counter = new SearchCommandCounter();
        await using var context = DatabaseFixture.CreateContext(counter);
        var caller = new Mock<ICurrentUserService>();
        var service = new MenuAuthoringSearchService(
            context, caller.Object, Options.Create(new MenuAuthoringPaginationSettings()));
        var stopwatch = Stopwatch.StartNew();

        var page = await service.SearchAsync("searchperf", null, null, 24, CancellationToken.None);

        stopwatch.Stop();
        page.Items.Should().HaveCount(24);
        counter.ReadCount.Should().Be(4,
            "the search queries the three candidate sources once each and loads decisions only for the bounded page");
        _output.WriteLine(
            $"Representative menu-authoring search: {stopwatch.ElapsedMilliseconds} ms, {counter.ReadCount} SQL reads, 45 bundles, 44 products, 26 components, 654 ingredients, 40 option sets.");
    }

    private async Task SeedRepresentativeCatalogueAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var ingredients = Enumerable.Range(0, 654)
            .Select(index => new GlobalIngredient
            {
                Id = Guid.NewGuid(),
                DefaultName = $"SearchPerf ingredient {index:D3}",
                IsActive = true,
                Kind = index % 3 == 0 ? IngredientKind.Sauce : IngredientKind.Ingredient,
                Origin = LibraryOrigin.Custom,
                CreatedBy = Actor
            })
            .ToArray();
        var regularProducts = Enumerable.Range(0, 44)
            .Select(index => NewProduct(Guid.NewGuid(), $"SearchPerf product {index:D3}"))
            .ToArray();
        var components = Enumerable.Range(0, 26)
            .Select(index =>
            {
                var product = NewProduct(Guid.NewGuid(), $"SearchPerf component {index:D3}");
                product.IsComponent = true;
                return product;
            })
            .ToArray();
        var bundles = Enumerable.Range(0, 45)
            .Select(index => NewBundle(index, regularProducts[index % regularProducts.Length]))
            .ToArray();
        var sets = Enumerable.Range(0, 40)
            .Select(index => new OptionSet
            {
                Id = Guid.NewGuid(),
                Kind = OptionSetKind.Sauce,
                Name = $"SearchPerf option set {index:D3}",
                NormalizedName = $"searchperf option set {index:D3}",
                Status = OptionSetStatus.Active,
                CreatedBy = Actor,
                Entries =
                [
                    new OptionSetEntry
                    {
                        Id = Guid.NewGuid(),
                        Name = ingredients[index].DefaultName,
                        GlobalIngredientId = ingredients[index].Id,
                        DisplayOrder = 0,
                        IsEnabled = true,
                        CreatedBy = Actor
                    }
                ]
            })
            .ToArray();

        context.AddRange(ingredients);
        context.AddRange(regularProducts);
        context.AddRange(components);
        context.AddRange(bundles.Select(bundle => bundle.Product));
        context.AddRange(sets);
        await context.SaveChangesAsync();
    }

    private static (Product Product, MenuDefinition Definition) NewBundle(int index, Product choice)
    {
        var bundle = NewProduct(Guid.NewGuid(), $"SearchPerf bundle {index:D3}");
        bundle.Type = ProductType.Menu;
        var definition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = bundle.Id,
            Product = bundle,
            IsAlwaysAvailable = true,
            CreatedBy = Actor
        };
        var section = new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = definition.Id,
            MenuDefinition = definition,
            Name = "Select one",
            DisplayOrder = 0,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedBy = Actor
        };
        section.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = section.Id,
            MenuSection = section,
            ProductId = choice.Id,
            Product = choice,
            DisplayOrder = 0,
            CreatedBy = Actor
        });
        definition.Sections.Add(section);
        bundle.MenuDefinition = definition;
        return (bundle, definition);
    }

    private static Product NewProduct(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        BasePrice = 1m,
        Type = ProductType.MainItem,
        IsActive = true,
        IsAvailable = true,
        CreatedBy = Actor
    };

    private sealed class SearchCommandCounter : DbCommandInterceptor
    {
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
