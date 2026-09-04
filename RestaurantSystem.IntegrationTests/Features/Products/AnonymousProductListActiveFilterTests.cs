using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Features.ApiTokens;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// Who may see a deactivated product on <c>GET /api/Products</c> (backend #438). Hiding one used to
/// be OPT-IN, per caller: three of the five callers passed <c>isActive=true</c> and two did not. The
/// web guest menu was ACCIDENTALLY safe — a client-side filter drops them after the server has
/// already sent them — and the mobile category browse showed them, so an owner switching a dish off
/// did not remove it from that menu.
/// <para>
/// Over HTTP, never through the handler: the subject is WHO IS CALLING, and a handler-level test
/// supplies that itself. The identity has to come from a real request or the test is choosing the
/// answer it then asserts.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class AnonymousProductListActiveFilterTests : ApiTokenScopeTestBase
{
    private const string ServedName = "438 Served Dish";
    private const string WithdrawnName = "438 Withdrawn Dish";

    public AnonymousProductListActiveFilterTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task A_guest_never_sees_a_deactivated_product()
    {
        AuthenticateAsAnonymous();

        var names = await ListedNamesAsync();

        names.Should().NotContain(WithdrawnName);
        names.Should().Contain(ServedName,
            "the served dish must still be listed — otherwise this passes on an empty list");
    }

    /// <summary>
    /// The filter is FORCED for a guest, not merely defaulted. Asking for the deactivated ones by
    /// name must not become a way to enumerate what the owner switched off.
    /// </summary>
    [Fact]
    public async Task A_guest_asking_for_inactive_products_gets_none()
    {
        AuthenticateAsAnonymous();

        var items = await ListedAsync("&isActive=false");

        items.Should().NotContain(p => p.Name == WithdrawnName);
        items.Should().OnlyContain(p => p.IsActive);
    }

    [Fact]
    public async Task A_signed_in_CUSTOMER_is_still_a_guest()
    {
        AuthenticateAsUser();

        (await ListedNamesAsync()).Should().NotContain(WithdrawnName);
    }

    /// <summary>
    /// Row 5 of the issue's table: the admin list MUST see inactive items — that is the whole point
    /// of an Active toggle — and it does not pass a filter, so the default has to stay unfiltered
    /// for back-of-house.
    /// </summary>
    [Fact]
    public async Task An_admin_still_sees_a_deactivated_product_by_default()
    {
        AuthenticateAsAdmin();

        (await ListedNamesAsync()).Should().Contain(WithdrawnName);
    }

    /// <summary>
    /// Not only Admin: the till (`useTakeOrder`) and the side-item pickers run as Server/Cashier,
    /// and they read the same list. `IsStaff`, not `IsAdmin`, is the line.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.Cashier)]
    public async Task Back_of_house_still_sees_a_deactivated_product_by_default(UserRole role)
    {
        AuthenticateAsRole(role);

        (await ListedNamesAsync()).Should().Contain(WithdrawnName);
    }

    [Fact]
    public async Task An_admin_can_still_ask_for_the_active_ones_only()
    {
        AuthenticateAsAdmin();

        var items = await ListedAsync("&isActive=true");

        items.Should().NotContain(p => p.Name == WithdrawnName);
        items.Should().Contain(p => p.Name == ServedName);
    }

    /// <summary>
    /// The third caller shape the issue named, pinned rather than left to be discovered: a machine
    /// token is neither a guest session nor a staff session, and
    /// <c>ApiTokenAuthenticationHandler</c> deliberately stamps it with the <c>Admin</c> role so it
    /// satisfies the existing <c>[RequireAdmin]</c> endpoints. It therefore keeps the back-of-house
    /// default here. A token is issued by the owner and bounded by its scopes; treating it as a
    /// guest would be a second policy decision, not this one.
    /// </summary>
    [Fact]
    public async Task A_machine_token_keeps_the_back_of_house_default()
    {
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.MenuRead]));

        (await ListedNamesAsync()).Should().Contain(WithdrawnName);
    }

    private async Task<IReadOnlyList<ProductSummaryDto>> ListedAsync(string extraQuery = "")
    {
        var response = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>(
            $"/api/Products?page=1&pageSize=200{extraQuery}");
        return response!.Data!.Items;
    }

    private async Task<IReadOnlyList<string>> ListedNamesAsync() =>
        (await ListedAsync()).Select(p => p.Name).ToList();

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Products.Add(Dish(ServedName, isActive: true));
        context.Products.Add(Dish(WithdrawnName, isActive: false));

        await context.SaveChangesAsync();
    }

    private static Product Dish(string name, bool isActive) => new()
    {
        Name = name,
        BasePrice = 14m,
        Type = ProductType.MainItem,
        IsActive = isActive,
        // Deliberately available: `IsAvailable` is the sold-out flag and a guest SHOULD see one of
        // those, marked. Only `IsActive` — the owner switching a dish off the menu — is withheld,
        // so a test that confused the two would still be red here.
        IsAvailable = true,
        CreatedBy = "test"
    };
}
