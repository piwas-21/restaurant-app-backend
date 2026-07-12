using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

// End-to-end characterization of the admin CustomerDiscounts surface, added
// alongside the refactor that thinned the controller (moved DB reads into
// ICustomerDiscountService and de-duplicated the 4x DTO mapping). Real HTTP
// through the auth/routing/EF pipeline against the Testcontainers Postgres —
// no mocks. Asserts status codes AND payloads/enrichment so any behavioural
// drift fails the test.
//
// NOTE on the "Unknown" enrichment fallback: CustomerDiscountRule.UserId has an
// enforced FK to AspNetUsers (OnDelete Cascade), so an orphan discount — the
// only way to reach the "Unknown" email/name fallback — cannot exist in the DB
// and therefore cannot be produced through HTTP. That defensive fallback is
// characterized directly in CustomerDiscountRuleMapperTests.
public class CustomerDiscountsControllerTests : IntegrationTestBase
{
    private const string BaseUrl = "/api/admin/customerdiscounts";
    private static readonly Guid SeededUserId = Guid.Parse(TestAuthHandler.UserId);       // test@example.com / "Test User"
    private static readonly Guid SeededAdminId = Guid.Parse(TestAuthHandler.AdminUserId);  // admin@example.com / "Admin User"

    public CustomerDiscountsControllerTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    private async Task<CustomerDiscountRule> SeedDiscountAsync(Action<CustomerDiscountRule>? configure = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = SeededUserId,
            Name = "Seeded Discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            UsageCount = 0,
            CreatedBy = "seed",
            CreatedAt = DateTime.UtcNow,
        };
        configure?.Invoke(discount);

        db.CustomerDiscountRules.Add(discount);
        await db.SaveChangesAsync();
        return discount;
    }

    private async Task<bool> DiscountIsActiveAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var discount = await db.CustomerDiscountRules.AsNoTracking().FirstAsync(d => d.Id == id);
        return discount.IsActive;
    }

    // ---------------------------------------------------------------- GET all

    [Fact]
    public async Task GetAll_NoFilter_ReturnsAllDiscounts_WithUserEnrichment()
    {
        AuthenticateAsAdmin();
        var seeded = await SeedDiscountAsync(d => d.Name = "Enriched One");

        var result = await GetFromJsonAsync<ApiResponse<List<CustomerDiscountRuleDto>>>(BaseUrl);

        result!.Success.Should().BeTrue();
        var dto = result.Data!.Should().ContainSingle(d => d.Id == seeded.Id).Subject;
        dto.Name.Should().Be("Enriched One");
        dto.UserId.Should().Be(SeededUserId);
        dto.UserEmail.Should().Be("test@example.com");
        dto.UserName.Should().Be("Test User");
        dto.DiscountType.Should().Be("Percentage");
        dto.DiscountValue.Should().Be(10m);
    }

    [Fact]
    public async Task GetAll_UserIdFilter_ReturnsOnlyThatUsersDiscounts()
    {
        AuthenticateAsAdmin();
        await SeedDiscountAsync(d => d.Name = "For Test User");
        var adminDiscount = await SeedDiscountAsync(d =>
        {
            d.UserId = SeededAdminId;
            d.Name = "For Admin User";
        });

        var result = await GetFromJsonAsync<ApiResponse<List<CustomerDiscountRuleDto>>>($"{BaseUrl}?userId={SeededAdminId}");

        result!.Success.Should().BeTrue();
        result.Data!.Should().OnlyContain(d => d.UserId == SeededAdminId);
        var dto = result.Data!.Should().ContainSingle().Subject;
        dto.Id.Should().Be(adminDiscount.Id);
        dto.UserEmail.Should().Be("admin@example.com");
        dto.UserName.Should().Be("Admin User");
    }

    [Fact]
    public async Task GetAll_ActiveOnly_FiltersByValidityWindowAndUsageCount()
    {
        AuthenticateAsAdmin();
        var now = DateTime.UtcNow;
        var active = await SeedDiscountAsync(d => d.Name = "Active");
        await SeedDiscountAsync(d => { d.Name = "Inactive"; d.IsActive = false; });
        await SeedDiscountAsync(d => { d.Name = "Expired"; d.ValidFrom = now.AddDays(-10); d.ValidUntil = now.AddDays(-1); });
        await SeedDiscountAsync(d => { d.Name = "Exhausted"; d.MaxUsageCount = 1; d.UsageCount = 1; });

        var result = await GetFromJsonAsync<ApiResponse<List<CustomerDiscountRuleDto>>>($"{BaseUrl}?activeOnly=true");

        result!.Success.Should().BeTrue();
        var dto = result.Data!.Should().ContainSingle().Subject;
        dto.Id.Should().Be(active.Id);
        dto.Name.Should().Be("Active");
    }

    // --------------------------------------------------------------- GET byId

    [Fact]
    public async Task GetById_Existing_ReturnsEnrichedDto()
    {
        AuthenticateAsAdmin();
        var seeded = await SeedDiscountAsync(d =>
        {
            d.Name = "Lookup Me";
            d.DiscountType = DiscountType.FixedAmount;
            d.DiscountValue = 7.5m;
            d.MaxUsageCount = 3;
        });

        var result = await GetFromJsonAsync<ApiResponse<CustomerDiscountRuleDto>>($"{BaseUrl}/{seeded.Id}");

        result!.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(seeded.Id);
        result.Data.Name.Should().Be("Lookup Me");
        result.Data.DiscountType.Should().Be("FixedAmount");
        result.Data.DiscountValue.Should().Be(7.5m);
        result.Data.MaxUsageCount.Should().Be(3);
        result.Data.UserEmail.Should().Be("test@example.com");
        result.Data.UserName.Should().Be("Test User");
    }

    [Fact]
    public async Task GetById_Unknown_ReturnsNotFound()
    {
        AuthenticateAsAdmin();

        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var result = await ReadResponseAsync<ApiResponse<CustomerDiscountRuleDto>>(response);
        result!.Success.Should().BeFalse();
        result.Errors.Should().Contain("Customer discount not found");
    }

    // ---------------------------------------------------------------- POST

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation_AndPersists()
    {
        AuthenticateAsAdmin();
        var request = new CreateCustomerDiscountRuleDto
        {
            UserId = SeededUserId,
            Name = "Created QA",
            DiscountType = "Percentage",
            DiscountValue = 15m,
            MinOrderAmount = 20m,
            IsActive = true,
        };

        var response = await PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await ReadResponseAsync<ApiResponse<CustomerDiscountRuleDto>>(response);
        created!.Success.Should().BeTrue();
        created.Data!.Id.Should().NotBeEmpty();
        created.Data.Name.Should().Be("Created QA");
        created.Data.DiscountValue.Should().Be(15m);
        created.Data.UserEmail.Should().Be("test@example.com");
        created.Data.UserName.Should().Be("Test User");

        // Round-trip: the new rule is retrievable by its id.
        var fetched = await GetFromJsonAsync<ApiResponse<CustomerDiscountRuleDto>>($"{BaseUrl}/{created.Data.Id}");
        fetched!.Success.Should().BeTrue();
        fetched.Data!.Name.Should().Be("Created QA");
    }

    [Fact]
    public async Task Create_UnknownUser_ReturnsBadRequest()
    {
        AuthenticateAsAdmin();
        var request = new CreateCustomerDiscountRuleDto
        {
            UserId = Guid.NewGuid(),
            Name = "No Such User",
            DiscountType = "Percentage",
            DiscountValue = 10m,
        };

        var response = await PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadResponseAsync<ApiResponse<CustomerDiscountRuleDto>>(response);
        result!.Success.Should().BeFalse();
        result.Errors.Should().Contain("User not found");
    }

    [Fact]
    public async Task Create_InvalidDiscountType_ReturnsBadRequest()
    {
        AuthenticateAsAdmin();
        var request = new CreateCustomerDiscountRuleDto
        {
            UserId = SeededUserId,
            Name = "Bad Type",
            DiscountType = "NotARealType",
            DiscountValue = 10m,
        };

        var response = await PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadResponseAsync<ApiResponse<CustomerDiscountRuleDto>>(response);
        result!.Success.Should().BeFalse();
        result.Errors.Should().Contain("Invalid discount type. Use 'Percentage' or 'FixedAmount'");
    }

    // ---------------------------------------------------------------- PUT

    [Fact]
    public async Task Update_Valid_Returns200_AndPersistsChanges()
    {
        AuthenticateAsAdmin();
        var seeded = await SeedDiscountAsync(d => d.Name = "Before");
        var request = new UpdateCustomerDiscountRuleDto
        {
            Name = "After",
            DiscountType = "FixedAmount",
            DiscountValue = 5m,
            IsActive = false,
        };

        var response = await PutAsJsonAsync($"{BaseUrl}/{seeded.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadResponseAsync<ApiResponse<CustomerDiscountRuleDto>>(response);
        updated!.Success.Should().BeTrue();
        updated.Data!.Name.Should().Be("After");
        updated.Data.DiscountType.Should().Be("FixedAmount");
        updated.Data.DiscountValue.Should().Be(5m);
        updated.Data.IsActive.Should().BeFalse();
        updated.Data.UserEmail.Should().Be("test@example.com");

        var fetched = await GetFromJsonAsync<ApiResponse<CustomerDiscountRuleDto>>($"{BaseUrl}/{seeded.Id}");
        fetched!.Data!.Name.Should().Be("After");
        fetched.Data.DiscountValue.Should().Be(5m);
    }

    [Fact]
    public async Task Update_InvalidDiscountType_ReturnsBadRequest()
    {
        AuthenticateAsAdmin();
        var seeded = await SeedDiscountAsync();
        var request = new UpdateCustomerDiscountRuleDto
        {
            Name = "Whatever",
            DiscountType = "Bogus",
            DiscountValue = 5m,
        };

        var response = await PutAsJsonAsync($"{BaseUrl}/{seeded.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadResponseAsync<ApiResponse<CustomerDiscountRuleDto>>(response);
        result!.Success.Should().BeFalse();
        result.Errors.Should().Contain("Invalid discount type. Use 'Percentage' or 'FixedAmount'");
    }

    [Fact]
    public async Task Update_Unknown_ReturnsNotFound()
    {
        AuthenticateAsAdmin();
        var request = new UpdateCustomerDiscountRuleDto
        {
            Name = "Ghost",
            DiscountType = "Percentage",
            DiscountValue = 10m,
        };

        var response = await PutAsJsonAsync($"{BaseUrl}/{Guid.NewGuid()}", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var result = await ReadResponseAsync<ApiResponse<CustomerDiscountRuleDto>>(response);
        result!.Success.Should().BeFalse();
    }

    // ---------------------------------------------------------------- DELETE

    [Fact]
    public async Task Delete_Existing_Returns200_AndDeactivates()
    {
        AuthenticateAsAdmin();
        var seeded = await SeedDiscountAsync(d => d.IsActive = true);

        var response = await Client.DeleteAsync($"{BaseUrl}/{seeded.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadResponseAsync<ApiResponse<object>>(response);
        result!.Success.Should().BeTrue();

        // Delete is a soft deactivate — row survives with IsActive = false.
        (await DiscountIsActiveAsync(seeded.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_Unknown_ReturnsNotFound()
    {
        AuthenticateAsAdmin();

        var response = await Client.DeleteAsync($"{BaseUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var result = await ReadResponseAsync<ApiResponse<object>>(response);
        result!.Success.Should().BeFalse();
    }
}
