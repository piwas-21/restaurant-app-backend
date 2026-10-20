using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Features.Basket;

namespace RestaurantSystem.IntegrationTests.Features.Addresses;

public sealed partial class CustomerAddressesTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("staff")]
    public async Task Every_native_address_writer_requires_a_real_current_customer_token(string mode)
    {
        var owner = await CustomerAsync();
        Client.DefaultRequestHeaders.Authorization = mode switch
        {
            "none" => null,
            "malformed" => new AuthenticationHeaderValue("Bearer", "invalid-token"),
            _ => new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services,
                mode == "staff" ? UserRole.Cashier : UserRole.Customer, userId: owner.ToString(), expired: mode == "expired"))
        };
        foreach (var action in new[] { "create", "update", "default", "remove" })
        {
            using var response = await MutateAsync(action, Id(1));
            response.StatusCode.Should().Be(mode == "staff" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        }
        await using (var verify = DatabaseFixture.CreateContext())
            (await verify.UserAddresses.CountAsync(value => value.UserId == owner)).Should().Be(0);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, userId: owner.ToString()));
        using var control = await MutateAsync("create", Id(1));
        control.StatusCode.Should().Be(HttpStatusCode.OK, await control.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("missing")]
    [InlineData("deleted")]
    public async Task Native_writers_refuse_removed_or_changed_database_customers_even_with_an_old_customer_claim(string mode)
    {
        var owner = await CustomerAsync(role: mode == "staff" ? UserRole.Admin : UserRole.Customer);
        if (mode != "staff")
        {
            await using var setup = DatabaseFixture.CreateContext();
            var user = await setup.Users.FindAsync(owner);
            if (mode == "deleted") user!.IsDeleted = true;
            else setup.Users.Remove(user!);
            await setup.SaveChangesAsync();
        }
        foreach (var action in new[] { "create", "update", "default", "remove" })
        {
            using var response = await MutateAsync(action, Id(1));
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        }
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.UserAddresses.CountAsync(value => value.UserId == owner)).Should().Be(0);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("default")]
    [InlineData("remove")]
    public async Task A_real_foreign_or_deleted_address_never_changes_either_owners_default(string action)
    {
        var owner = await CustomerAsync();
        var foreign = await CustomerAsync(select: false);
        await using (var setup = DatabaseFixture.CreateContext())
        {
            var own = Address(owner, 1);
            own.IsDefault = true;
            var other = Address(foreign, 2);
            other.IsDefault = true;
            var deleted = Address(owner, 3);
            deleted.IsDeleted = true;
            setup.UserAddresses.AddRange(own, other, deleted);
            await setup.SaveChangesAsync();
        }
        foreach (var id in new[] { Id(2), Id(3), Guid.NewGuid() })
        {
            using var response = await MutateAsync(action, id);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        }
        await using var verify = DatabaseFixture.CreateContext();
        // soft-delete-bypass: verify the deleted fixture remains deleted and is never revived.
        var rows = await verify.UserAddresses.IgnoreQueryFilters().AsNoTracking().ToArrayAsync();
        rows.Single(value => value.Id == Id(1)).IsDefault.Should().BeTrue();
        var untouched = rows.Single(value => value.Id == Id(2));
        untouched.UserId.Should().Be(foreign);
        untouched.IsDefault.Should().BeTrue();
        untouched.AddressLine1.Should().Be("Fixture street");
        untouched.IsDeleted.Should().BeFalse();
        rows.Single(value => value.Id == Id(3)).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Native_customer_role_is_checked_after_a_competing_account_lifecycle_lock_is_released()
    {
        var owner = await CustomerAsync();
        await using var lifecycle = DatabaseFixture.CreateContext();
        await using var transaction = await lifecycle.Database.BeginTransactionAsync();
        var held = await lifecycle.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM "Users" WHERE id = {owner} FOR UPDATE
            """).ToArrayAsync();
        held.Should().Equal(owner);
        var pending = MutateAsync("create", Id(1));
        bool observed;
        try
        {
            observed = await WaitForNativeOwnerGuardAsync();
            await lifecycle.Users.Where(user => user.Id == owner)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.Role, UserRole.Admin));
        }
        finally { await transaction.CommitAsync(); }
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        observed.Should().BeTrue("the native writer must reach the account guard before its role is changed");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.UserAddresses.CountAsync(value => value.UserId == owner)).Should().Be(0);
    }

    private async Task<bool> WaitForNativeOwnerGuardAsync()
    {
        await using var observer = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await observer.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event_type = 'Lock'
                AND query LIKE '%FOR NO KEY UPDATE%'
            """, observer);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await command.ExecuteScalarAsync() is long count && count > 0) return true;
            await Task.Delay(25);
        }
        return false;
    }
}
