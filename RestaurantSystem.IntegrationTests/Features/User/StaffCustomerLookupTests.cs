using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.User;

[Collection("Database Lane 4")]
public sealed class StaffCustomerLookupTests : IntegrationTestBase
{
    public StaffCustomerLookupTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    public async Task Staff_can_search_active_customers_by_name_or_email(UserRole role)
    {
        var customerId = await SeedCustomerAsync("Ada", "Lovelace", "ada.lookup@example.com", 42);
        await SeedCustomerAsync("Ada", "Deleted", "ada.deleted@example.com", 99, isDeleted: true);

        AuthenticateAsRole(role);
        var response = await Client.GetAsync("/api/User/customer-lookup?search=ada");
        var body = await ReadResponseAsync<ApiResponse<List<StaffCustomerLookupDto>>>(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Success.Should().BeTrue();
        body.Data.Should().ContainSingle(customer => customer.Id == customerId);
        body.Data![0].CurrentPoints.Should().Be(42);
        body.Data[0].FullName.Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task Lookup_returns_minimal_identity_and_rejects_short_search()
    {
        await SeedCustomerAsync("Ada", "Lovelace", "ada.lookup@example.com", 42);
        AuthenticateAsServer();

        var shortSearch = await Client.GetAsync("/api/User/customer-lookup?search=a");
        shortSearch.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var response = await Client.GetAsync(
            "/api/User/customer-lookup?search=ada.lookup%40example.com");
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.Should().NotContain("\"role\"");
        json.Should().NotContain("\"metadata\"");
        JsonSerializer.Deserialize<ApiResponse<List<StaffCustomerLookupDto>>>(json, JsonOptions)
            ?.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task Customer_and_anonymous_callers_cannot_use_staff_lookup()
    {
        AuthenticateAsUser();
        (await Client.GetAsync("/api/User/customer-lookup?search=ada")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        AuthenticateAsAnonymous();
        (await Client.GetAsync("/api/User/customer-lookup?search=ada")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> SeedCustomerAsync(
        string firstName, string lastName, string email, int points, bool isDeleted = false)
    {
        var customer = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            Role = UserRole.Customer,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            RefreshToken = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        await using var context = DatabaseFixture.CreateContext();
        context.Users.Add(customer);
        context.FidelityPointBalances.Add(new FidelityPointBalance
        {
            UserId = customer.Id,
            CurrentPoints = points,
            TotalEarnedPoints = points,
            LastUpdated = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();
        return customer.Id;
    }

    private void AuthenticateAsServer() => AuthenticateAsRole(UserRole.Server);
}

[Collection("Database Lane 4")]
public sealed class StaffCustomerLookupModuleGateTests : IntegrationTestBase
{
    public StaffCustomerLookupModuleGateTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantModules>();
        services.AddSingleton<ITenantModules>(new TenantModules(
            Options.Create(new ModuleSettings { Enabled = "core", Enforce = true }),
            NullLogger<TenantModules>.Instance));
    }

    [Fact]
    public async Task Lookup_is_hidden_when_neither_staff_module_is_enabled()
    {
        AuthenticateAsAdmin();

        var response = await Client.GetAsync("/api/User/customer-lookup?search=ada");
        var body = await ReadResponseAsync<ApiResponse<object>>(response);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body!.ErrorCode.Should().Be(ErrorCodes.ModuleNotEnabled);
    }
}
