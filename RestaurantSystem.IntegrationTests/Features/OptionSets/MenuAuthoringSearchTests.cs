using System.Data.Common;
using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
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
    public async Task Search_normalizes_accents_and_punctuation_ranks_relevance_and_keeps_active_filters()
    {
        AuthenticateAsAdmin();
        var exactId = Guid.NewGuid();
        var prefixProductId = Guid.NewGuid();
        var containsProductId = Guid.NewGuid();
        var inactiveProductId = Guid.NewGuid();
        var unavailableProductId = Guid.NewGuid();
        var deletedProductId = Guid.NewGuid();
        var translatedIngredientId = Guid.NewGuid();
        var containsIngredientId = Guid.NewGuid();
        var archivedIngredientId = Guid.NewGuid();
        var optionSetId = Guid.NewGuid();
        var archivedOptionSetId = Guid.NewGuid();
        var exactProduct = NewProduct(exactId, "Café-Probe");
        var prefixProduct = NewProduct(prefixProductId, "Café-Probe bowl");
        var containsProduct = NewProduct(containsProductId, "Special Café-Probe plate");
        var inactiveProduct = NewProduct(inactiveProductId, "Café-Probe inactive");
        inactiveProduct.IsActive = false;
        var unavailableProduct = NewProduct(unavailableProductId, "Café-Probe unavailable");
        unavailableProduct.IsAvailable = false;
        var deletedProduct = NewProduct(deletedProductId, "Café-Probe deleted");
        deletedProduct.IsDeleted = true;
        var bundle = NewBundle(0, exactProduct);
        bundle.Product.Name = "Café-Probe bundle";
        var translatedIngredient = NewIngredient(translatedIngredientId, "Probe seasoning");
        translatedIngredient.Translations.Add(new GlobalIngredientTranslation
        {
            Id = Guid.NewGuid(),
            GlobalIngredientId = translatedIngredientId,
            GlobalIngredient = translatedIngredient,
            LanguageCode = "fr",
            Name = "Café-Probe seasoning",
            CreatedBy = Actor
        });
        var containsIngredient = NewIngredient(containsIngredientId, "Kebab Café-Probe spice");
        var archivedIngredient = NewIngredient(archivedIngredientId, "Café-Probe archived");
        archivedIngredient.ArchivedAt = DateTime.UtcNow;
        var optionSet = NewOptionSet(optionSetId, "Café-Probe sauces", OptionSetStatus.Active);
        var archivedOptionSet = NewOptionSet(archivedOptionSetId, "Café-Probe archived", OptionSetStatus.Archived);

        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Products.AddRange(exactProduct, prefixProduct, containsProduct, inactiveProduct,
                unavailableProduct, deletedProduct, bundle.Product);
            context.GlobalIngredients.AddRange(translatedIngredient, containsIngredient, archivedIngredient);
            context.OptionSets.AddRange(optionSet, archivedOptionSet);
            await context.SaveChangesAsync();
        }

        var page = await GetFromJsonAsync<ApiResponse<MenuAuthoringSearchPageDto>>(
            "/api/MenuAuthoring/search?q=cafe%20probe&limit=24");
        var ids = page!.Data!.Items.Select(candidate => candidate.Id).ToArray();

        ids.Take(5).Should().Equal(
            new[] { exactId, prefixProductId, bundle.Product.Id, translatedIngredientId, optionSetId },
            "exact name results precede prefix matches, which use a stable candidate type order");
        ids.Should().Contain(containsProductId).And.Contain(containsIngredientId);
        ids.Should().NotContain(inactiveProductId).And.NotContain(unavailableProductId)
            .And.NotContain(deletedProductId).And.NotContain(archivedIngredientId).And.NotContain(archivedOptionSetId);
        page.Data.Items.Should().Contain(candidate => candidate.Id == exactId && candidate.MatchSource == "name");

        var firstPage = await GetFromJsonAsync<ApiResponse<MenuAuthoringSearchPageDto>>(
            "/api/MenuAuthoring/search?q=cafe%20probe&limit=5");
        firstPage!.Data!.Items.Select(candidate => candidate.Id)
            .Should().Equal(exactId, prefixProductId, bundle.Product.Id, translatedIngredientId, optionSetId);
        firstPage.Data.NextCursor.Should().NotBeNullOrWhiteSpace();
        var cursor = Uri.EscapeDataString(firstPage.Data.NextCursor!);
        var secondPage = await GetFromJsonAsync<ApiResponse<MenuAuthoringSearchPageDto>>(
            $"/api/MenuAuthoring/search?q=cafe%20probe&limit=5&cursor={cursor}");
        secondPage!.Data!.Items.Select(candidate => candidate.Id).ToArray()
            .Should().Equal(new[] { containsProductId, containsIngredientId },
                "keyset paging must continue across the relevance-rank boundary without duplicates");

        var noNearMatch = await GetFromJsonAsync<ApiResponse<MenuAuthoringSearchPageDto>>(
            "/api/MenuAuthoring/search?q=caff%20probe&limit=24");
        noNearMatch!.Data!.Items.Should().BeEmpty("search uses normalized substring matches, not fuzzy similarity");
    }

    [Fact]
    public async Task Search_normalized_substring_predicates_can_use_the_trigram_indexes()
    {
        await using var context = DatabaseFixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SET LOCAL enable_seqscan = off";
        await command.ExecuteNonQueryAsync();

        var probes = new (string IndexName, string Sql)[]
        {
            ("ix_products_menu_authoring_name_trgm", """
                EXPLAIN (COSTS OFF) SELECT id FROM "Products"
                WHERE is_active AND is_available AND NOT is_deleted
                    AND public.menu_authoring_search_normalize(name)
                        ILIKE public.menu_authoring_search_pattern(@searchText)
                """),
            ("ix_global_ingredients_menu_authoring_name_trgm", """
                EXPLAIN (COSTS OFF) SELECT id FROM global_ingredients
                WHERE is_active AND archived_at IS NULL AND NOT is_deleted
                    AND public.menu_authoring_search_normalize(default_name)
                        ILIKE public.menu_authoring_search_pattern(@searchText)
                """),
            ("ix_global_ingredient_translations_menu_authoring_name_trgm", """
                EXPLAIN (COSTS OFF) SELECT id FROM global_ingredient_translations
                WHERE public.menu_authoring_search_normalize(name)
                    ILIKE public.menu_authoring_search_pattern(@searchText)
                """),
            ("ix_option_sets_menu_authoring_name_trgm", """
                EXPLAIN (COSTS OFF) SELECT id FROM "OptionSets"
                WHERE status = 0 AND public.menu_authoring_search_normalize(name)
                    ILIKE public.menu_authoring_search_pattern(@searchText)
                """),
            ("ix_option_set_translations_menu_authoring_name_trgm", """
                EXPLAIN (COSTS OFF) SELECT id FROM "OptionSetTranslations"
                WHERE public.menu_authoring_search_normalize(name)
                    ILIKE public.menu_authoring_search_pattern(@searchText)
                """)
        };

        foreach (var probe in probes)
        {
            command.Parameters.Clear();
            var parameter = command.CreateParameter();
            parameter.ParameterName = "searchText";
            parameter.Value = "Café-Probe";
            command.Parameters.Add(parameter);
            command.CommandText = probe.Sql;
            await using var reader = await command.ExecuteReaderAsync();
            var planLines = new List<string>();
            while (await reader.ReadAsync())
            {
                planLines.Add(reader.GetString(0));
            }

            string.Join(Environment.NewLine, planLines).Should().Contain(probe.IndexName);
        }
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

    private static GlobalIngredient NewIngredient(Guid id, string name) => new()
    {
        Id = id,
        DefaultName = name,
        IsActive = true,
        Kind = IngredientKind.Ingredient,
        Origin = LibraryOrigin.Custom,
        CreatedBy = Actor
    };

    private static OptionSet NewOptionSet(Guid id, string name, OptionSetStatus status) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToLowerInvariant().Replace("café", "cafe", StringComparison.Ordinal).Replace('-', ' '),
        Kind = OptionSetKind.Sauce,
        Status = status,
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
