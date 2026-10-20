using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Addresses;

[Collection("Database Lane 4")]
public sealed partial class CustomerAddressesTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Path = "/api/customer-mobile/account/addresses";
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
        // The fixture's default policy names its test scheme; legacy [Authorize] routes must
        // use the same real JWT owner as native [RequireRole] routes in compatibility controls.
        services.PostConfigure<AuthorizationOptions>(options => options.DefaultPolicy =
            new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build());
    }

    [Fact]
    public async Task Pages_are_bounded_ordered_owned_and_exclude_audit_location_and_deleted_data()
    {
        var owner = await CustomerAsync();
        var foreign = await CustomerAsync(select: false);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.UserAddresses.AddRange(Enumerable.Range(1, 22).Reverse().Select(index => Address(owner, index)));
            var deleted = Address(owner, 23);
            deleted.IsDeleted = true;
            context.UserAddresses.Add(deleted);
            context.UserAddresses.Add(Address(foreign, 24));
            await context.SaveChangesAsync();
        }
        using var first = await Client.GetAsync(Path + "?schemaVersion=1");
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        first.Headers.CacheControl!.NoStore.Should().BeTrue();
        var page = (await first.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        page.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("id").GetGuid()).Should().Equal(Enumerable.Range(1, 20).Select(Id));
        items[0].EnumerateObject().Select(value => value.Name).Should().BeEquivalentTo(
            "id", "label", "addressLine1", "addressLine2", "city", "state", "postalCode", "country",
            "phone", "isDefault", "deliveryInstructions");
        items[0].GetProperty("addressLine2").GetString().Should().Be("Floor 2");
        items[0].GetProperty("phone").GetString().Should().Be("+41790000000");
        page.GetProperty("nextAfterId").GetGuid().Should().Be(Id(20));
        using var second = await Client.GetAsync(Path + "?schemaVersion=1&afterId=" + Id(20));
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var next = (await second.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        var tail = next.GetProperty("items").EnumerateArray().ToArray();
        tail.Select(item => item.GetProperty("id").GetGuid()).Should().Equal(Id(21), Id(22));
        tail[0].GetProperty("isDefault").GetBoolean().Should().BeTrue();
        next.GetProperty("nextAfterId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Empty_owner_is_distinct_from_a_missing_account_and_cursor_never_selects_an_owner()
    {
        var owner = await CustomerAsync();
        var foreign = await CustomerAsync(select: false);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.UserAddresses.Add(Address(foreign, 1));
            await context.SaveChangesAsync();
        }
        using var response = await Client.GetAsync(Path + "?schemaVersion=1&afterId=" + Id(1) + "&userId=" + foreign);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        data.GetProperty("items").GetArrayLength().Should().Be(0);
        data.GetProperty("nextAfterId").ValueKind.Should().Be(JsonValueKind.Null);
        owner.Should().NotBe(foreign);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("staff")]
    public async Task Current_database_customer_is_required_even_with_a_customer_claim(string mode)
    {
        var owner = await CustomerAsync(role: mode == "staff" ? UserRole.Admin : UserRole.Customer);
        if (mode != "staff")
        {
            await using var context = DatabaseFixture.CreateContext();
            var user = await context.Users.FindAsync(owner);
            if (mode == "deleted") user!.IsDeleted = true;
            else context.Users.Remove(user!);
            await context.SaveChangesAsync();
        }
        (await Client.GetAsync(Path + "?schemaVersion=1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("staff")]
    public async Task Authentication_and_role_cannot_be_replaced_by_query_navigation(string mode)
    {
        var owner = await CustomerAsync();
        Client.DefaultRequestHeaders.Authorization = mode switch
        {
            "none" => null,
            "malformed" => new AuthenticationHeaderValue("Bearer", "invalid-token"),
            _ => new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services,
                mode == "staff" ? UserRole.Cashier : UserRole.Customer, userId: owner.ToString(), expired: mode == "expired"))
        };
        (await Client.GetAsync(Path + "?schemaVersion=1&userId=" + owner)).StatusCode
            .Should().Be(mode == "staff" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?schemaVersion=2")]
    [InlineData("?schemaVersion=1&afterId=invalid")]
    [InlineData("?schemaVersion=1&afterId=00000000-0000-0000-0000-000000000000")]
    public async Task Unsupported_version_or_cursor_is_refused_with_an_independent_valid_control(string query)
    {
        await CustomerAsync();
        (await Client.GetAsync(Path + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.GetAsync(Path + "?schemaVersion=1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static Guid Id(int index) => Guid.Parse($"{index:x8}-0000-4000-8000-000000000000");
    private static UserAddress Address(Guid owner, int index) => new()
    {
        Id = Id(index),
        UserId = owner,
        Label = "Home",
        AddressLine1 = "Fixture street",
        AddressLine2 = "Floor 2",
        City = "Fixture city",
        PostalCode = "1201",
        Country = "CH",
        Phone = "+41790000000",
        IsDefault = index == 21,
        Latitude = 46.2,
        Longitude = 6.1,
        DeliveryInstructions = "Ring once",
        CreatedBy = nameof(CustomerAddressesTests)
    };
    private async Task<Guid> CustomerAsync(bool select = true, UserRole role = UserRole.Customer)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"native-address-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Native",
            LastName = "Fixture",
            Role = role,
            CreatedBy = nameof(CustomerAddressesTests),
            RefreshToken = string.Empty
        };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        if (select) Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, userId: user.Id.ToString()));
        return user.Id;
    }
}
