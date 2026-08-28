using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientsQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using System.Data.Common;

namespace RestaurantSystem.IntegrationTests.Features.GlobalIngredients;

/// <summary>
/// S3 — the reverse link: "used on N items", the <c>USAGE</c> column of the approved picker screen.
///
/// <para>
/// Two things make this worth testing rather than reading. The count is over DISTINCT products, not
/// over ingredient rows — nothing stops one product carrying two ingredients copied from the same
/// library row, and "used on 2 items" would then be a lie about one item. And it must cost the same
/// whatever the catalog size: the picker loads the WHOLE ~650-row library in one response, so a
/// per-row count is 650 round trips per modal open, which is the failure mode
/// <see cref="TheLibraryList_CostsTwoQueries_WhateverTheCatalogSize"/> exists to make impossible.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class GlobalIngredientUsageCountTests : Infrastructure.IntegrationTestBase
{
    private const string UnusedName = "S3 Usage — used by nothing";
    private const string OnceName = "S3 Usage — used by one";
    private const string TwiceOnOneProductName = "S3 Usage — twice on one product";
    private const string ManyName = "S3 Usage — used by three";
    private const string DeletedProductName = "S3 Usage — used by a deleted product";

    private readonly CountingInterceptor _commands = new();

    public GlobalIngredientUsageCountTests(Infrastructure.DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ---- the count itself ----------------------------------------------------------------------

    [Fact]
    public async Task UsageCount_IsZero_ForARowNoProductUses() =>
        (await CountForAsync(UnusedName)).Should().Be(0);

    [Fact]
    public async Task UsageCount_IsOne_ForARowOneProductUses() =>
        (await CountForAsync(OnceName)).Should().Be(1);

    [Fact]
    public async Task UsageCount_CountsManyProducts() =>
        (await CountForAsync(ManyName)).Should().Be(3);

    /// <summary>
    /// The reason it is <c>COUNT(DISTINCT product)</c> and not a row count: an admin who added the
    /// same library row twice to one recipe has one item at stake, not two, and the number is about
    /// to label an "Archive" button.
    /// </summary>
    [Fact]
    public async Task UsageCount_CountsAProductOnce_WhenItLinksTwiceToTheSameRow() =>
        (await CountForAsync(TwiceOnOneProductName)).Should().Be(1);

    /// <summary>
    /// Counted through <c>Products</c>, so the soft-delete query filter applies: a deleted product
    /// does not use anything, and counting it would leave rows that can never fall back to a plain
    /// delete.
    /// </summary>
    [Fact]
    public async Task UsageCount_IgnoresADeletedProduct() =>
        (await CountForAsync(DeletedProductName)).Should().Be(0);

    // ---- the cost ------------------------------------------------------------------------------

    /// <summary>
    /// TWO queries — the page and one aggregate over it — with a catalog of hundreds of rows. A
    /// per-row count would show up here as one command per row, which is what the picker cannot
    /// afford.
    /// <para>
    /// It runs the handler directly rather than over HTTP on purpose: a request also issues the
    /// auth and middleware queries that have nothing to do with the claim, and an exact count is the
    /// only assertion that can fail on an N+1.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheLibraryList_CostsTwoQueries_WhateverTheCatalogSize()
    {
        // The lane's own data source, plus a counter on this context alone. An interceptor
        // registered in the test host's DI is never reached — the context there comes from Aspire's
        // registration, and it resolves none.
        using var context = DatabaseFixture.CreateContext(_commands);
        var handler = new GetGlobalIngredientsQueryHandler(context);

        _commands.Reset();
        var response = await handler.Handle(new GetGlobalIngredientsQuery(), CancellationToken.None);

        response.Data!.Count.Should().BeGreaterThan(25,
            "the proof is worthless against a catalog of one row");
        _commands.Count.Should().Be(2,
            "one query for the page and one aggregate for every count on it");
        response.Data.Should().Contain(i => i.DefaultName == ManyName && i.UsedOnProductCount == 3,
            "and the counts are still right");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<int> CountForAsync(string defaultName)
    {
        var response = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>("/api/global-ingredients");

        return response!.Data!.Single(i => i.DefaultName == defaultName).UsedOnProductCount;
    }

    private sealed class CountingInterceptor : DbCommandInterceptor
    {
        private int _count;

        public int Count => _count;

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var unused = NewGlobal(UnusedName);
        var once = NewGlobal(OnceName);
        var twice = NewGlobal(TwiceOnOneProductName);
        var many = NewGlobal(ManyName);
        var onADeletedProduct = NewGlobal(DeletedProductName);

        var oneProduct = NewProduct("S3 Usage Product A");
        oneProduct.DetailedIngredients.Add(NewIngredient("Tomato", once));

        var twiceProduct = NewProduct("S3 Usage Product B");
        twiceProduct.DetailedIngredients.Add(NewIngredient("Basil", twice));
        twiceProduct.DetailedIngredients.Add(NewIngredient("Extra basil", twice));

        var manyProducts = Enumerable.Range(1, 3)
            .Select(index =>
            {
                var product = NewProduct($"S3 Usage Product C{index}");
                product.DetailedIngredients.Add(NewIngredient("Cheese", many));
                return product;
            })
            .ToList();

        var deletedProduct = NewProduct("S3 Usage Deleted Product");
        deletedProduct.IsDeleted = true;
        deletedProduct.DeletedAt = DateTime.UtcNow;
        deletedProduct.DeletedBy = "test";
        deletedProduct.DetailedIngredients.Add(NewIngredient("Olive", onADeletedProduct));

        // Filler, so the cost proof runs against a catalog and not against five rows: an N+1 over
        // one page is indistinguishable from a single query.
        var filler = Enumerable.Range(1, 24).Select(index => NewGlobal($"S3 Usage Filler {index:D2}"));

        context.AddRange(unused, once, twice, many, onADeletedProduct);
        context.AddRange(filler);
        context.AddRange(oneProduct, twiceProduct, deletedProduct);
        context.AddRange(manyProducts);
        await context.SaveChangesAsync();
    }

    private static GlobalIngredient NewGlobal(string defaultName) => new()
    {
        DefaultName = defaultName,
        IsActive = true,
        CreatedBy = "test",
    };

    private static Product NewProduct(string name) => new()
    {
        Name = name,
        BasePrice = 10m,
        IsActive = true,
        IsAvailable = true,
        Type = ProductType.MainItem,
        CreatedBy = "test",
    };

    private static ProductIngredient NewIngredient(string name, GlobalIngredient global) => new()
    {
        Name = name,
        IsActive = true,
        MaxQuantity = 1,
        GlobalIngredient = global,
        CreatedBy = "test",
    };
}
