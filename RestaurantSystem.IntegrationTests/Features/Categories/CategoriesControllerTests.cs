using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Categories.Commands.CreateCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Commands.ReorderCategoriesCommand;
using RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Categories;

// End-to-end coverage of the Categories CRUD surface (DEV-PHASES W2, D9):
// real HTTP through the mediator/validation/EF pipeline against the
// Testcontainers Postgres — no mocks. Exercises the RBAC seam ([RequireAdmin]
// → 403 for the default Customer principal), the "not found" envelope
// convention (200 + Success:false, not HTTP 404), validation, and soft delete.
public class CategoriesControllerTests : IntegrationTestBase
{
    public CategoriesControllerTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    private async Task<CategoryDto> FirstSeededCategoryAsync()
    {
        var list = await GetFromJsonAsync<ApiResponse<PagedResult<CategoryDto>>>("/api/categories");
        list!.Data!.Items.Should().NotBeEmpty();
        return list.Data.Items.First();
    }

    [Fact]
    public async Task GetCategories_ReturnsSeededCategories()
    {
        var result = await GetFromJsonAsync<ApiResponse<PagedResult<CategoryDto>>>("/api/categories");

        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCountGreaterThanOrEqualTo(3);
        result.Data.Items.Select(c => c.Name).Should().Contain("Main Course");
    }

    [Fact]
    public async Task GetCategoryById_Existing_ReturnsDetail()
    {
        var seeded = await FirstSeededCategoryAsync();

        var result = await GetFromJsonAsync<ApiResponse<CategoryDetailDto>>($"/api/categories/{seeded.Id}");

        result!.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(seeded.Id);
        result.Data.Name.Should().Be(seeded.Name);
    }

    [Fact]
    public async Task GetCategoryById_UnknownId_ReturnsNotFoundEnvelope()
    {
        // The handler returns a Failure envelope (Ok(200) + Success:false), not a 404.
        var response = await Client.GetAsync($"/api/categories/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResponseAsync<ApiResponse<CategoryDetailDto>>(response);
        result!.Success.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetCategoryProducts_ForExistingCategory_ReturnsOk()
    {
        // Regression for #138: this endpoint used to return HTTP 500 because the
        // DTO projection was built inside the IQueryable Select, pushing an
        // untranslatable Descriptions.GroupBy(..).First().ToDictionary(..) into
        // SQL — EF threw at query execution regardless of row count. Empty-path
        // guard: even with no linked products the query must now return OK.
        var seeded = await FirstSeededCategoryAsync();

        var response = await Client.GetAsync($"/api/categories/{seeded.Id}/products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResponseAsync<ApiResponse<PagedResult<CategoryProductDto>>>(response);
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCategoryProducts_WithLinkedProductVariations_BuildsDedupedContent()
    {
        // Regression for #138 exercising the ACTUAL projection path: link a
        // product whose variation carries two descriptions sharing a language
        // code, then read it back. Pre-fix this 500'd at query translation; the
        // fix materializes then projects in memory, where the
        // Descriptions.GroupBy(lang).First().ToDictionary(lang) dedup runs.
        Guid categoryId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            categoryId = (await db.Categories.FirstAsync()).Id;
            // CreatedBy is a required member; SaveChanges overwrites it, but the
            // compiler needs it set. Matches the seeder's CreatedBy = "seed".
            var product = new Product
            {
                Name = "Linked QA Product",
                BasePrice = 9.99m,
                IsActive = true,
                IsAvailable = true,
                CreatedBy = "test",
                Variations = new List<ProductVariation>
                {
                    new()
                    {
                        Name = "Large",
                        DisplayOrder = 1,
                        IsActive = true,
                        CreatedBy = "test",
                        Descriptions = new List<ProductVariationDescription>
                        {
                            new() { LanguageCode = "en", Name = "Large", CreatedBy = "test" },
                            new() { LanguageCode = "en", Name = "Large (duplicate language)", CreatedBy = "test" },
                        },
                    },
                },
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            db.ProductCategories.Add(new ProductCategory
            {
                ProductId = product.Id,
                CategoryId = categoryId,
                DisplayOrder = 1,
                IsPrimary = true,
                CreatedBy = "test",
            });
            await db.SaveChangesAsync();
        }

        var result = await GetFromJsonAsync<ApiResponse<PagedResult<CategoryProductDto>>>(
            $"/api/categories/{categoryId}/products");

        result!.Success.Should().BeTrue();
        var item = result.Data!.Items.Should().ContainSingle().Subject;
        item.Name.Should().Be("Linked QA Product");
        var variation = item.Variations.Should().ContainSingle().Subject;
        // Two "en" descriptions collapse to a single key via GroupBy(lang).First().
        variation.Content.Should().HaveCount(1).And.ContainKey("en");
    }

    [Fact]
    public async Task CreateCategory_AsAdmin_Succeeds_AndIsRetrievable()
    {
        AuthenticateAsAdmin();
        var command = new CreateCategoryCommand("Starters QA", "Small plates", true, 10);

        var response = await PostAsJsonAsync("/api/categories", command);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await ReadResponseAsync<ApiResponse<CategoryDto>>(response);
        created!.Success.Should().BeTrue();
        created.Data!.Name.Should().Be("Starters QA");
        created.Data.Id.Should().NotBeEmpty();

        // Round-trip: the new category is retrievable by its id.
        var fetched = await GetFromJsonAsync<ApiResponse<CategoryDetailDto>>($"/api/categories/{created.Data.Id}");
        fetched!.Success.Should().BeTrue();
        fetched.Data!.Name.Should().Be("Starters QA");
    }

    [Fact]
    public async Task CreateCategory_WithoutAdmin_IsForbidden()
    {
        // Default principal is Customer (authenticated, not admin) → [RequireAdmin] denies.
        AuthenticateAsUser();
        var command = new CreateCategoryCommand("Should Not Exist QA", null, true, 99);

        var response = await PostAsJsonAsync("/api/categories", command);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateCategory_EmptyName_ReturnsBadRequest()
    {
        AuthenticateAsAdmin();
        var command = new CreateCategoryCommand("", "no name", true, 1);

        var response = await PostAsJsonAsync("/api/categories", command);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateCategory_AsAdmin_ChangesName()
    {
        AuthenticateAsAdmin();
        var created = await ReadResponseAsync<ApiResponse<CategoryDto>>(
            await PostAsJsonAsync("/api/categories", new CreateCategoryCommand("Renameable QA", null, true, 20)));
        var id = created!.Data!.Id;

        var update = new UpdateCategoryCommand(id, "Renamed QA", "now with description", false, 21);
        var response = await PutAsJsonAsync($"/api/categories/{id}", update);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReadResponseAsync<ApiResponse<CategoryDto>>(response);
        updated!.Success.Should().BeTrue();
        updated.Data!.Name.Should().Be("Renamed QA");
        updated.Data.IsActive.Should().BeFalse();
    }

    // Attach a product to a category through a SEPARATE scope/DbContext on purpose: seeding via the
    // same context would let change-tracker fix-up populate `pc.Product` and mask the very bug these
    // tests exist for. `Id` is set explicitly because the product and its link are saved together.
    private async Task AttachProductAsync(Guid categoryId, string productName, decimal price)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = productName,
            BasePrice = price,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "integration-test"
        };
        db.Products.Add(product);
        db.ProductCategories.Add(new ProductCategory
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            CategoryId = categoryId,
            IsPrimary = true,
            CreatedBy = "integration-test"
        });
        await db.SaveChangesAsync();
    }

    // Regression: updating a category that HAS PRODUCTS used to 500.
    //
    // The response's ProductCount dereferences `pc.Product` in memory after materialisation, and the
    // handler included ProductCategories without ThenInclude(pc => pc.Product) — with lazy loading
    // off, the navigation was null and the Count lambda threw a NullReferenceException.
    //
    // It survived because every existing update test creates a BRAND-NEW, EMPTY category: with no
    // ProductCategories rows the lambda never runs, so the null is never dereferenced. Only a
    // category with at least one product reaches the bug — which is every real one, so in practice
    // the admin could not rename a category, toggle it, or save the order-type channel matrix
    // (ORDER-TYPE-AVAILABILITY-PLAN §9.1's surface) against live data at all.
    [Fact]
    public async Task UpdateCategory_WithProducts_Succeeds_AndReportsProductCount()
    {
        AuthenticateAsAdmin();
        var created = await ReadResponseAsync<ApiResponse<CategoryDto>>(
            await PostAsJsonAsync("/api/categories", new CreateCategoryCommand("Populated QA", null, true, 30)));
        var categoryId = created!.Data!.Id;

        // Attach a product directly: the point is the category's read-back projection, not the
        // product-creation contract.
        await AttachProductAsync(categoryId, "Populated QA Product", 9.5m);

        var update = new UpdateCategoryCommand(categoryId, "Populated QA Renamed", "still has a product", true, 30);
        var response = await PutAsJsonAsync($"/api/categories/{categoryId}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadResponseAsync<ApiResponse<CategoryDto>>(response);
        updated!.Success.Should().BeTrue();
        updated.Data!.Name.Should().Be("Populated QA Renamed");
        // The very projection that used to throw.
        updated.Data.ProductCount.Should().Be(1);
    }

    // NOTE: this exercises `UpdateCategoryCommand`, NOT the image handler. `UpdateCategoryImageCommand`
    // carries the identical defect and the identical fix, but is corrected BY INSPECTION and has no
    // test: its route is a multipart PUT and the NRE sits after a real image is stored, so covering
    // it means the suite's first multipart upload through image validation. Tracked as follow-up —
    // do not read the fix below as evidence for that handler.
    [Fact]
    public async Task UpdateCategory_WithProducts_PreservesOrderTypeMask()
    {
        AuthenticateAsAdmin();
        var created = await ReadResponseAsync<ApiResponse<CategoryDto>>(
            await PostAsJsonAsync("/api/categories", new CreateCategoryCommand("Channel QA", null, true, 31, 6)));
        var categoryId = created!.Data!.Id;
        created.Data.AvailableOrderTypes.Should().Be(6);

        await AttachProductAsync(categoryId, "Channel QA Product", 11m);

        // A rename that ECHOES the mask must keep it — the §9.1 contract, now on a populated
        // category so the 500 cannot mask the assertion.
        var update = new UpdateCategoryCommand(categoryId, "Channel QA Renamed", null, true, 31, 6);
        var response = await PutAsJsonAsync($"/api/categories/{categoryId}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadResponseAsync<ApiResponse<CategoryDto>>(response);
        updated!.Data!.AvailableOrderTypes.Should().Be(6);
    }

    [Fact]
    public async Task UpdateCategory_IdMismatch_ReturnsBadRequest()
    {
        AuthenticateAsAdmin();
        var routeId = Guid.NewGuid();
        var bodyWithDifferentId = new UpdateCategoryCommand(Guid.NewGuid(), "Mismatch QA", null, true, 1);

        var response = await PutAsJsonAsync($"/api/categories/{routeId}", bodyWithDifferentId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteCategory_EmptyCategory_AsAdmin_SoftDeletes()
    {
        AuthenticateAsAdmin();
        var created = await ReadResponseAsync<ApiResponse<CategoryDto>>(
            await PostAsJsonAsync("/api/categories", new CreateCategoryCommand("Deletable QA", null, true, 30)));
        var id = created!.Data!.Id;

        var response = await Client.DeleteAsync($"/api/categories/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResponseAsync<ApiResponse<string>>(response))!.Success.Should().BeTrue();

        // After soft delete it is no longer retrievable (Failure envelope).
        var fetched = await GetFromJsonAsync<ApiResponse<CategoryDetailDto>>($"/api/categories/{id}");
        fetched!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ReorderCategories_AsAdmin_Succeeds()
    {
        AuthenticateAsAdmin();
        var list = await GetFromJsonAsync<ApiResponse<PagedResult<CategoryDto>>>("/api/categories");
        var items = list!.Data!.Items.ToList();

        // Reverse the display order of the seeded categories.
        var orders = items
            .Select((c, i) => new CategoryOrderDto { CategoryId = c.Id, DisplayOrder = items.Count - i })
            .ToList();
        var command = new ReorderCategoriesCommand(orders);

        var response = await PutAsJsonAsync("/api/categories/reorder", command);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResponseAsync<ApiResponse<string>>(response))!.Success.Should().BeTrue();
    }
}
