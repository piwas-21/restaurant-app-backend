using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.User;

[Collection("Database Lane 2")]
public class CustomerProfilePhoneTests : IntegrationTestBase
{
    private const string OriginalPhone = "+41791234567";

    public CustomerProfilePhoneTests(DatabaseFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Omitted_or_null_phone_preserves_the_saved_number(bool includeNull)
    {
        await SeedPhoneAsync();
        var payload = new Dictionary<string, object?> { ["firstName"] = "Alex", ["lastName"] = "Guest" };
        if (includeNull) payload["phoneNumber"] = null;
        using var response = await Client.PutAsJsonAsync("/api/User/profile", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPhoneAsync()).Should().Be((OriginalPhone, true));
    }

    [Fact]
    public async Task Empty_phone_clears_storage_and_the_response()
    {
        await SeedPhoneAsync();
        using var response = await Client.PutAsJsonAsync("/api/User/profile",
            new { firstName = "Alex", lastName = "Guest", phoneNumber = "" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("data").GetProperty("phoneNumber").ValueKind
            .Should().Be(System.Text.Json.JsonValueKind.Null);
        (await ReadPhoneAsync()).Should().Be(((string?)null, false));
    }

    [Theory]
    [InlineData("+442079460001", HttpStatusCode.OK, false)]
    [InlineData("not-a-phone", HttpStatusCode.BadRequest, true)]
    [InlineData(OriginalPhone, HttpStatusCode.OK, true)]
    public async Task Supplied_phone_is_validated_before_storage(string phone, HttpStatusCode expected, bool confirmed)
    {
        await SeedPhoneAsync();
        using var response = await Client.PutAsJsonAsync("/api/User/profile",
            new { firstName = "Alex", lastName = "Guest", phoneNumber = phone });
        response.StatusCode.Should().Be(expected);
        (await ReadPhoneAsync()).Should().Be((expected == HttpStatusCode.OK ? phone : OriginalPhone, confirmed));
    }

    private async Task SeedPhoneAsync()
    {
        AuthenticateAsUser();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = Guid.Parse(TestAuthHandler.UserId);
        var user = await context.Users.SingleAsync(candidate => candidate.Id == userId);
        user.PhoneNumber = OriginalPhone;
        user.PhoneNumberConfirmed = true;
        await context.SaveChangesAsync();
    }

    private async Task<(string? Number, bool Confirmed)> ReadPhoneAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = Guid.Parse(TestAuthHandler.UserId);
        var stored = await context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);
        return (stored.PhoneNumber, stored.PhoneNumberConfirmed);
    }
}
