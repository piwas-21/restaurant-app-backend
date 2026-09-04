using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// Backend #413. <c>POST /api/Products</c> carried <c>[ApiScope(MenuWrite)]</c> and nothing else.
/// That attribute looks like a guard and is not one — its own doc comment says so: pure metadata,
/// read by <c>ApiTokenScopeFilter</c> to narrow what a MACHINE token may reach, and INERT for a
/// human caller. With no <c>FallbackPolicy</c> in <c>Program.cs</c>, the action was open, and an
/// anonymous create was confirmed reachable on production (400 on an invalid body, never 401).
///
/// <para>
/// <see cref="Common.MutatingEndpointAuthorizationCoverageTests"/> holds the general rule; this
/// file pins the behaviour end to end, through the real pipeline, for the one endpoint that was
/// actually open. Both are wanted: the coverage test would stay green if the attribute stopped
/// being ENFORCED, and a behavioural test alone would not stop the next unmarked endpoint.
/// </para>
/// </summary>
[Collection("Database Lane 3")]
public class ProductMutationAuthorizationTests : IntegrationTestBase
{
    public ProductMutationAuthorizationTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    /// <summary>
    /// A body the create path actually accepts — <c>basePrice</c>, a category and a content row are
    /// all required by <c>CreateProductCommandValidator</c>. It is deliberately valid so that the
    /// admin case below observes the AUTHORIZATION decision rather than a validation refusal that
    /// would look identical whether the caller was let through or not.
    /// </summary>
    private async Task<object> CreatableProductAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var categoryId = (await context.Categories.AsNoTracking().FirstAsync()).Id;

        return new
        {
            name = $"auth-probe-{Guid.NewGuid():N}",
            basePrice = 9.99m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 5,
            type = (int)ProductType.MainItem,
            kitchenType = (int)KitchenType.None,
            displayOrder = 0,
            categoryIds = new[] { categoryId },
            primaryCategoryId = categoryId,
            content = new Dictionary<string, object>
            {
                ["en"] = new { name = "Auth probe", description = "d" },
            },
        };
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_create_a_product()
    {
        AuthenticateAsAnonymous();

        var response = await PostAsJsonAsync("/api/Products", await CreatableProductAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "product creation is an admin action; before #413 this answered 200 on a valid body — "
            + "an anonymous caller could create a product on a live tenant");
    }

    /// <summary>
    /// A signed-in CUSTOMER is the case the 401 test cannot reach: they clear authentication and
    /// fail only on the role. Without this, replacing <c>[RequireAdmin]</c> with a bare
    /// <c>[Authorize]</c> would leave every other assertion in this file green.
    /// </summary>
    [Fact]
    public async Task A_signed_in_customer_cannot_create_a_product()
    {
        AuthenticateAsUser();

        var response = await PostAsJsonAsync("/api/Products", await CreatableProductAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The control that keeps the fix honest: a blanket refusal would satisfy both assertions above
    /// while taking the admin menu editor offline.
    /// <para>
    /// It is not the only admin create over HTTP in the suite —
    /// <c>ComponentProductCatalogTests.The_flag_round_trips_through_create_and_update</c> does one
    /// too, and asserts more about the result. What this adds is isolation: that test would also go
    /// red for a dozen reasons that have nothing to do with authorization, so on its own it cannot
    /// tell a broken create contract from a revoked admin.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_admin_can_still_create_a_product()
    {
        AuthenticateAsAdmin();

        var response = await PostAsJsonAsync("/api/Products", await CreatableProductAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the admin menu editor is the endpoint's real caller — if this is red the fix took a "
            + "working surface offline, which no amount of green in the refusal tests would show");
    }
}
