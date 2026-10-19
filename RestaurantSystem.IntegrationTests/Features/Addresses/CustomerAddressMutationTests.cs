using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.IntegrationTests.Features.Addresses;

public sealed partial class CustomerAddressesTests
{
    [Fact]
    public async Task Native_mutations_share_owned_rules_and_return_only_delivery_display_fields()
    {
        var owner = await CustomerAsync();
        var foreign = await CustomerAsync(select: false);
        await using (var seed = DatabaseFixture.CreateContext())
        {
            var own = Address(owner, 1);
            own.IsDefault = true;
            var other = Address(foreign, 2);
            other.IsDefault = true;
            seed.UserAddresses.AddRange(own, other);
            await seed.SaveChangesAsync();
        }
        using var created = await MutateAsync("create", Id(1));
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        created.Headers.CacheControl!.NoStore.Should().BeTrue();
        var data = (await created.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        data.EnumerateObject().Select(field => field.Name).Should().BeEquivalentTo("schemaVersion", "address");
        data.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        var address = data.GetProperty("address");
        address.EnumerateObject().Select(field => field.Name).Should().BeEquivalentTo(
            "id", "label", "addressLine1", "addressLine2", "city", "state", "postalCode", "country",
            "phone", "isDefault", "deliveryInstructions");
        var id = address.GetProperty("id").GetGuid();
        id.Should().NotBeEmpty();
        address.GetProperty("isDefault").GetBoolean().Should().BeTrue();
        var input = NativeContent();
        input["label"] = "Work";
        using var updated = await MutateAsync("update", id, new { schemaVersion = 1, address = input });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await updated.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data
            .GetProperty("address").GetProperty("label").GetString().Should().Be("Work");
        foreach (var action in new[] { "default", "remove" })
        {
            var target = action == "default" ? Id(1) : id;
            using var response = await MutateAsync(action, target);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var result = (await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
            result.EnumerateObject().Select(field => field.Name).Should().BeEquivalentTo("schemaVersion", "addressId", "state");
            result.GetProperty("addressId").GetGuid().Should().Be(target);
            result.GetProperty("state").GetString().Should().Be(action == "default" ? "default" : "removed");
        }
        await using var verify = DatabaseFixture.CreateContext();
        // soft-delete-bypass: this test must distinguish a soft-deleted row from physical loss.
        var rows = await verify.UserAddresses.IgnoreQueryFilters().AsNoTracking().ToArrayAsync();
        rows.Single(value => value.Id == id).IsDeleted.Should().BeTrue();
        rows.Single(value => value.Id == Id(1)).IsDefault.Should().BeTrue();
        var untouched = rows.Single(value => value.Id == Id(2));
        untouched.UserId.Should().Be(foreign);
        untouched.IsDefault.Should().BeTrue();
        untouched.Label.Should().Be("Home");
    }

    [Fact]
    public async Task Native_edit_preserves_unexposed_coordinates_while_legacy_full_update_still_updates_them()
    {
        var owner = await CustomerAsync();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.UserAddresses.Add(Address(owner, 1));
            await seed.SaveChangesAsync();
        }
        var content = NativeContent();
        content["addressLine1"] = "Updated street";
        using var response = await MutateAsync("update", Id(1), new { schemaVersion = 1, address = content });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using (var verify = DatabaseFixture.CreateContext())
        {
            var row = await verify.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == Id(1));
            row.Latitude.Should().Be(46.2);
            row.Longitude.Should().Be(6.1);
            row.AddressLine1.Should().Be("Updated street");
            row.UserId.Should().Be(owner);
        }
        content["id"] = Id(1);
        content["latitude"] = 47.3;
        content["longitude"] = 7.4;
        using var legacy = await Client.PutAsJsonAsync("/api/Addresses/" + Id(1), content);
        legacy.StatusCode.Should().Be(HttpStatusCode.OK, await legacy.Content.ReadAsStringAsync());
        (await legacy.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Success.Should().BeTrue();
        await using var final = DatabaseFixture.CreateContext();
        var saved = await final.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == Id(1));
        saved.Latitude.Should().Be(47.3);
        saved.Longitude.Should().Be(7.4);
    }

    [Theory]
    [InlineData("omitted-version")]
    [InlineData("wrong-version")]
    [InlineData("null-address")]
    [InlineData("owner")]
    [InlineData("nested-owner")]
    [InlineData("coordinates")]
    [InlineData("internal-policy")]
    [InlineData("missing-default")]
    [InlineData("null-default")]
    [InlineData("NUL")]
    [InlineData("overflow")]
    public async Task Native_body_refuses_underposting_extra_identity_policy_location_and_invalid_storage(string mode)
    {
        var owner = await CustomerAsync();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.UserAddresses.Add(Address(owner, 1));
            await seed.SaveChangesAsync();
        }
        var input = NativeContent();
        var body = new Dictionary<string, object?> { ["schemaVersion"] = 1, ["address"] = input };
        switch (mode)
        {
            case "omitted-version": body.Remove("schemaVersion"); break;
            case "wrong-version": body["schemaVersion"] = 2; break;
            case "null-address": body["address"] = null; break;
            case "owner": body["userId"] = Guid.NewGuid(); break;
            case "nested-owner": input["userId"] = Guid.NewGuid(); break;
            case "coordinates": input["latitude"] = 46.2; break;
            case "internal-policy": input["requireCustomer"] = false; break;
            case "missing-default": input.Remove("isDefault"); break;
            case "null-default": input["isDefault"] = null; break;
            case "NUL": input["label"] = "Home\0"; break;
            case "overflow": input["label"] = new string('x', 51); break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
        foreach (var action in new[] { "create", "update" })
        {
            using var response = await MutateAsync(action, Id(1), body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        }
        await using (var verify = DatabaseFixture.CreateContext())
        {
            (await verify.UserAddresses.CountAsync(value => value.UserId == owner)).Should().Be(1);
            (await verify.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == Id(1))).AddressLine1
                .Should().Be("Fixture street");
        }
        using var control = await MutateAsync("create", Id(1));
        control.StatusCode.Should().Be(HttpStatusCode.OK, await control.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("omitted-version")]
    [InlineData("wrong-version")]
    [InlineData("owner")]
    public async Task Native_actions_require_their_exact_versioned_body(string mode)
    {
        var owner = await CustomerAsync();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.UserAddresses.Add(Address(owner, 1));
            await seed.SaveChangesAsync();
        }
        var body = new Dictionary<string, object?> { ["schemaVersion"] = 1 };
        if (mode == "omitted-version") body.Clear();
        if (mode == "wrong-version") body["schemaVersion"] = 2;
        if (mode == "owner") body["userId"] = Guid.NewGuid();
        foreach (var action in new[] { "default", "remove" })
        {
            using var response = await MutateAsync(action, Id(1), body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        }
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == Id(1))).IsDeleted.Should().BeFalse();
        using var control = await MutateAsync("default", Id(1));
        control.StatusCode.Should().Be(HttpStatusCode.OK, await control.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> MutateAsync(string action, Guid id, object? body = null)
    {
        body ??= action is "create" or "update" ? new { schemaVersion = 1, address = NativeContent() }
            : new { schemaVersion = 1 };
        return action switch
        {
            "create" => Client.PostAsJsonAsync(Path, body),
            "update" => Client.PutAsJsonAsync(Path + "/" + id, body),
            "default" => Client.PostAsJsonAsync(Path + "/" + id + "/set-default", body),
            "remove" => Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, Path + "/" + id) { Content = JsonContent.Create(body) }),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }
    private static Dictionary<string, object?> NativeContent() => new()
    {
        ["label"] = "Home",
        ["addressLine1"] = "Fixture street",
        ["addressLine2"] = "Floor 2",
        ["city"] = "Fixture city",
        ["state"] = null,
        ["postalCode"] = "1201",
        ["country"] = "CH",
        ["phone"] = "+41790000000",
        ["isDefault"] = true,
        ["deliveryInstructions"] = "Ring once"
    };
}
