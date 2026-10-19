using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Addresses.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Addresses;

[Collection("Database Lane 4")]
public sealed class AddressWriteConcurrencyTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("default")]
    [InlineData("delete")]
    public async Task Concurrent_owned_writes_wait_for_the_account_guard_and_leave_at_most_one_default(string action)
    {
        AuthenticateAsUser();
        var ids = action == "create" ? new[] { Guid.NewGuid(), Guid.NewGuid() }
            : new[] { await SeedAsync("First"), await SeedAsync("Second") };
        await using var guard = DatabaseFixture.CreateContext();
        await guard.Database.OpenConnectionAsync();
        await using var transaction = await guard.Database.BeginTransactionAsync();
        var owner = Guid.Parse(TestAuthHandler.UserId);
        var held = await guard.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM "Users" WHERE id = {owner} FOR UPDATE
            """).ToArrayAsync();
        held.Should().Equal(owner);
        var requests = ids.Select((id, index) => WriteAsync(action, id, "Address " + index)).ToArray();
        bool blocked;
        try { blocked = await WaitForTwoBlockedWritersAsync(); }
        finally { await transaction.RollbackAsync(); }
        var responses = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            // The external database guard also blocks old create FK checks after both unsets;
            // releasing it makes the missing serialization produce two defaults deterministically.
            blocked.Should().BeTrue("both writes must respect the account lifecycle guard");
            foreach (var response in responses)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                body.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            }
            await using var context = DatabaseFixture.CreateContext();
            var saved = await context.UserAddresses.AsNoTracking().Where(value => value.UserId == owner).ToArrayAsync();
            saved.Count(value => value.IsDefault && !value.IsDeleted).Should().Be(action == "delete" ? 0 : 1);
            saved.Count(value => !value.IsDeleted).Should().Be(action == "delete" ? 0 : 2);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Deleted_accounts_cannot_create_update_delete_or_select_an_address()
    {
        AuthenticateAsUser();
        var id = await SeedAsync("Original");
        await using (var context = DatabaseFixture.CreateContext())
        {
            var user = await context.Users.FindAsync(Guid.Parse(TestAuthHandler.UserId));
            user!.IsDeleted = true;
            await context.SaveChangesAsync();
        }
        foreach (var action in new[] { "create", "update", "default", "delete" })
        {
            using var response = await WriteAsync(action, id, "Changed");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        await using var verify = DatabaseFixture.CreateContext();
        var saved = await verify.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == id);
        saved.Label.Should().Be("Original");
        saved.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Foreign_and_unknown_addresses_never_clear_the_current_default()
    {
        AuthenticateAsUser();
        var id = await SeedAsync("Original", isDefault: true);
        var foreignOwner = Guid.NewGuid();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            var email = $"foreign-address-{foreignOwner:N}@example.test";
            seed.Users.Add(new ApplicationUser
            {
                Id = foreignOwner,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = "Foreign",
                LastName = "Fixture",
                Role = UserRole.Customer,
                RefreshToken = string.Empty,
                CreatedBy = nameof(AddressWriteConcurrencyTests)
            });
            await seed.SaveChangesAsync();
        }
        var foreignAddress = await SeedAsync("Foreign", isDefault: true, owner: foreignOwner);
        foreach (var target in new[] { foreignAddress, Guid.NewGuid() })
        {
            foreach (var action in new[] { "update", "default", "delete" })
            {
                using var response = await WriteAsync(action, target, "Changed");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                body.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            }
        }
        await using var context = DatabaseFixture.CreateContext();
        (await context.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == id)).IsDefault.Should().BeTrue();
        var untouched = await context.UserAddresses.AsNoTracking().SingleAsync(value => value.Id == foreignAddress);
        untouched.Label.Should().Be("Foreign");
        untouched.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Address_guard_allows_unrelated_account_foreign_key_checks()
    {
        var owner = Guid.Parse(TestAuthHandler.UserId);
        await using var guarded = DatabaseFixture.CreateContext();
        await using var transaction = await AddressWriteTransaction.BeginAsync(guarded, owner, CancellationToken.None);
        await using var unrelated = DatabaseFixture.CreateContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Order.UserId inserts acquire this PostgreSQL foreign-key lock. Keep the fixture
        // independent of order creation, its journal and the address writer under test.
        var referenced = await unrelated.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM "Users" WHERE id = {owner} FOR KEY SHARE
            """).ToArrayAsync(timeout.Token);

        referenced.Should().Equal(new[] { owner }, "address changes must allow unrelated account FK checks");
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Missing_owner_releases_its_short_transaction_without_creating_rows()
    {
        await using var context = DatabaseFixture.CreateContext();
        var missing = Guid.NewGuid();
        var begin = () => AddressWriteTransaction.BeginAsync(context, missing, CancellationToken.None);
        await begin.Should().ThrowAsync<NotFoundException>();
        context.Database.CurrentTransaction.Should().BeNull();
        (await context.UserAddresses.AnyAsync(value => value.UserId == missing)).Should().BeFalse();
    }

    [Fact]
    public async Task Existing_transaction_is_refused_without_disposing_the_callers_transaction()
    {
        await using var context = DatabaseFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var begin = () => AddressWriteTransaction.BeginAsync(context, Guid.Parse(TestAuthHandler.UserId), CancellationToken.None);
        await begin.Should().ThrowAsync<ConflictException>();
        context.Database.CurrentTransaction.Should().BeSameAs(transaction);
        await transaction.RollbackAsync();
    }

    private Task<HttpResponseMessage> WriteAsync(string action, Guid id, string label) => action switch
    {
        "create" => Client.PostAsJsonAsync("/api/Addresses", Content(label)),
        "update" => Client.PutAsJsonAsync("/api/Addresses/" + id, new { id, label, addressLine1 = "Fixture street", city = "Fixture city", postalCode = "1201", country = "CH", isDefault = true }),
        "default" => Client.PostAsync("/api/Addresses/" + id + "/set-default", null),
        "delete" => Client.DeleteAsync("/api/Addresses/" + id),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
    private static object Content(string label) => new
    {
        label,
        addressLine1 = "Fixture street",
        city = "Fixture city",
        postalCode = "1201",
        country = "CH",
        isDefault = true
    };
    private async Task<Guid> SeedAsync(string label, bool isDefault = false, Guid? owner = null)
    {
        await using var context = DatabaseFixture.CreateContext();
        var address = new UserAddress
        {
            UserId = owner ?? Guid.Parse(TestAuthHandler.UserId),
            Label = label,
            AddressLine1 = "Fixture street",
            City = "Fixture city",
            PostalCode = "1201",
            Country = "CH",
            IsDefault = isDefault,
            CreatedBy = nameof(AddressWriteConcurrencyTests)
        };
        context.UserAddresses.Add(address);
        await context.SaveChangesAsync();
        return address.Id;
    }
    private async Task<bool> WaitForTwoBlockedWritersAsync()
    {
        await using var observer = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await observer.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event_type = 'Lock'
            """, observer);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await command.ExecuteScalarAsync() is long count && count >= 2) return true;
            await Task.Delay(25);
        }
        return false;
    }
}
