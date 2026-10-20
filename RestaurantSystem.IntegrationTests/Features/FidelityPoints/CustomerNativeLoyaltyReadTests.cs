using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

[Collection("Database Lane 4")]
public sealed partial class CustomerNativeLoyaltyReadTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Route = "/api/customer-mobile/loyalty";
    private static readonly Guid Owner = Guid.Parse(TestAuthHandler.UserId);
    private static readonly Guid Foreign = Guid.Parse(TestAuthHandler.AdminUserId);
    private static readonly DateTime Day = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Mock<ITenantModules> _modules = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
        services.PostConfigure<CustomerMobileSettings>(settings =>
        {
            settings.Enabled = true;
            settings.TenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
            settings.Lifecycle = "active";
            settings.EnabledCapabilities = ["loyalty"];
        });
        _modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns(true);
        _modules.SetupGet(value => value.IsEnforced).Returns(true);
        _modules.SetupGet(value => value.EnabledModules).Returns([ModuleIds.Core, ModuleIds.Loyalty]);
        services.RemoveAll<ITenantModules>();
        services.AddSingleton(_modules.Object);
    }

    [Fact]
    public async Task Recorded_balance_and_empty_account_have_no_money_or_spendability_claim()
    {
        Authenticate();
        var empty = await ReadAsync("/balance?schemaVersion=1");
        Assert.Equal(0, empty.GetProperty("currentPoints").GetInt32());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("lastUpdated").ValueKind);
        await using var context = DatabaseFixture.CreateContext();
        context.FidelityPointBalances.AddRange(
            new FidelityPointBalance
            {
                UserId = Owner,
                CurrentPoints = 415,
                TotalEarnedPoints = 700,
                TotalRedeemedPoints = 285,
                LastUpdated = Day,
                CreatedBy = "private-audit"
            },
            new FidelityPointBalance { UserId = Foreign, CurrentPoints = 99999, LastUpdated = Day, CreatedBy = "private-audit" });
        await context.SaveChangesAsync();
        var balance = await ReadAsync("/balance?schemaVersion=1&userId=" + Foreign);
        Assert.Equal(new[] { "schemaVersion", "currentPoints", "totalEarnedPoints", "totalRedeemedPoints", "lastUpdated" },
            balance.EnumerateObject().Select(property => property.Name));
        Assert.Equal(415, balance.GetProperty("currentPoints").GetInt32());
        Assert.Equal(700, balance.GetProperty("totalEarnedPoints").GetInt32());
        Assert.Equal(285, balance.GetProperty("totalRedeemedPoints").GetInt32());
        Assert.Equal(Day, balance.GetProperty("lastUpdated").GetDateTime());
    }

    [Fact]
    public async Task Ledger_pages_by_exact_UTC_timestamp_and_tie_id_without_staff_notes_or_foreign_rows()
    {
        Authenticate();
        await SeedAsync(Enumerable.Range(1, 23).Reverse().Select(index => Movement(index, Owner))
            .Concat([Movement(0, Foreign), Movement(24, null)]));
        var first = await ReadAsync("/history?schemaVersion=1&userId=" + Foreign);
        var items = first.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(Enumerable.Range(1, 20).Select(Id), items.Select(row => row.GetProperty("transactionId").GetGuid()));
        Assert.Equal(new[] { "transactionId", "kind", "points", "createdAt", "expiresAt" },
            items[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal("adjusted", items[0].GetProperty("kind").GetString());
        Assert.Equal(-1, items[0].GetProperty("points").GetInt32());
        var cursor = first.GetProperty("next");
        Assert.Equal(Day.AddTicks(1234560), cursor.GetProperty("createdAt").GetDateTime());
        var second = await ReadAsync("/history?schemaVersion=1&beforeCreatedAt="
            + Uri.EscapeDataString(cursor.GetProperty("createdAt").GetString()!)
            + "&beforeId=" + cursor.GetProperty("transactionId").GetGuid());
        Assert.Equal(Enumerable.Range(21, 3).Select(Id), second.GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("transactionId").GetGuid()));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("next").ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.Cashier)]
    public async Task Both_reads_require_the_authenticated_customer(UserRole? role)
    {
        if (role is not null) Authenticate(role.Value);
        foreach (var path in new[] { "/balance", "/history" })
            Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                (await Client.GetAsync(Route + path + "?schemaVersion=1")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?schemaVersion=0")]
    [InlineData("?schemaVersion=2")]
    public async Task Both_reads_require_schema_one(string query)
    {
        Authenticate();
        foreach (var path in new[] { "/balance", "/history" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(Route + path + query)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync(Route + path + "?schemaVersion=1")).StatusCode);
        }
    }

    [Theory]
    [InlineData("beforeCreatedAt=2026-11-01T00:00:00Z")]
    [InlineData("beforeId=11111111-1111-4111-8111-111111111111")]
    [InlineData("beforeCreatedAt=bad&beforeId=11111111-1111-4111-8111-111111111111")]
    [InlineData("beforeId=00000000-0000-0000-0000-000000000000&beforeCreatedAt=2026-11-01T00:00:00Z")]
    public async Task Partial_or_invalid_history_cursors_refuse_with_a_working_control(string query)
    {
        Authenticate();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(Route + "/history?schemaVersion=1&" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync(Route + "/history?schemaVersion=1")).StatusCode);
    }

    [Theory]
    [InlineData("read-only")]
    [InlineData("disabled")]
    [InlineData("capability")]
    [InlineData("module")]
    [InlineData("currency")]
    [InlineData("account")]
    [InlineData("role")]
    public async Task Fresh_capability_and_database_customer_are_required(string withdrawal)
    {
        Authenticate();
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync(Route + "/balance?schemaVersion=1")).StatusCode);
        using var scope = Factory.Services.CreateScope();
        var mobile = scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        if (withdrawal == "module") _modules.Setup(value => value.IsEnabled(ModuleIds.Loyalty)).Returns(false);
        else if (withdrawal == "currency")
        {
            await using var context = DatabaseFixture.CreateContext();
            await context.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(row => row.Currency, (string?)null));
        }
        else if (withdrawal == "capability") mobile.EnabledCapabilities = [];
        else if (withdrawal is "disabled" or "read-only") mobile.Lifecycle = withdrawal;
        else
        {
            await using var context = DatabaseFixture.CreateContext();
            var user = await context.Users.SingleAsync(value => value.Id == Owner);
            if (withdrawal == "account") user.IsDeleted = true; else user.Role = UserRole.Admin;
            await context.SaveChangesAsync();
        }
        foreach (var path in new[] { "/balance", "/history" })
            Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(Route + path + "?schemaVersion=1")).StatusCode);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("earned")]
    [InlineData("redeemed")]
    public async Task Negative_recorded_balance_requires_reconciliation(string field)
    {
        Authenticate();
        await using var context = DatabaseFixture.CreateContext();
        context.FidelityPointBalances.Add(new FidelityPointBalance
        {
            UserId = Owner,
            CurrentPoints = field == "current" ? -1 : 1,
            TotalEarnedPoints = field == "earned" ? -1 : 1,
            TotalRedeemedPoints = field == "redeemed" ? -1 : 0,
            LastUpdated = Day,
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.GetAsync(Route + "/balance?schemaVersion=1")).StatusCode);
    }

    private void Authenticate(UserRole role = UserRole.Customer) => Client.DefaultRequestHeaders.Authorization =
        new("Bearer", CustomerBasketTokens.Create(Factory.Services, role, userId: Owner.ToString()));
    private async Task<JsonElement> ReadAsync(string suffix)
    {
        using var response = await Client.GetAsync(Route + suffix);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var text = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "private-audit", "private-note", "description", "userId", "orderId", "orderTotal", "originalTransactionId", "availablePoints", "currentPointsValue" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
    }
    private async Task SeedAsync(IEnumerable<FidelityPointsTransaction> movements)
    {
        await using var context = DatabaseFixture.CreateContext();
        context.FidelityPointsTransactions.AddRange(movements);
        await context.SaveChangesAsync();
    }
    private static FidelityPointsTransaction Movement(int index, Guid? owner) => new()
    {
        Id = Id(index),
        UserId = owner,
        TransactionType = TransactionType.AdminAdjustment,
        Points = -index,
        CreatedAt = Day.AddTicks(1234560),
        Description = "private-note",
        CreatedBy = "private-audit",
        OriginalTransactionId = Guid.NewGuid(),
        ExpiresAt = Day.AddDays(20)
    };
    private static Guid Id(int index) => new(index, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 0);
}
