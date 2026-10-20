using System.Net;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Features.Basket;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCancellationTests
{
    [Theory]
    [InlineData("anonymous")]
    [InlineData("expired")]
    [InlineData("admin")]
    [InlineData("cashier")]
    [InlineData("server")]
    public async Task Only_valid_customer_authentication_can_prepare_submit_recover_or_stop(string mode)
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        if (mode == "anonymous") Client.DefaultRequestHeaders.Authorization = null;
        else Authenticate(mode switch { "admin" => UserRole.Admin, "cashier" => UserRole.Cashier, "server" => UserRole.Server, _ => UserRole.Customer }, mode == "expired");
        var expected = mode is "anonymous" or "expired" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await SendAsync("prepare", Request())).StatusCode);
        foreach (var path in new[] { "submit", "recover", "cancel-attempt" })
            Assert.Equal(expected, (await SendAsync(path, proof)).StatusCode);
        await AssertUntouchedAsync();
    }
    [Theory]
    [InlineData("foreign")]
    [InlineData("guest")]
    [InlineData("missing")]
    public async Task Missing_foreign_and_guest_bookings_have_the_same_private_refusal(string mode)
    {
        Authenticate();
        await using var db = DatabaseFixture.CreateContext();
        Guid? newOwner = null;
        if (mode == "foreign")
        {
            newOwner = Guid.NewGuid();
            db.Users.Add(new ApplicationUser { Id = newOwner.Value, UserName = "other", Email = "other@example.test", FirstName = "Other", LastName = "Customer", Role = UserRole.Customer, RefreshToken = "", CreatedBy = "test" });
            await db.SaveChangesAsync();
        }
        if (mode != "missing") await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerId, newOwner));
        var request = mode == "missing" ? Request() with { ReservationId = Guid.NewGuid() } : Request();
        using var response = await SendAsync("prepare", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("private@example.test", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    [Theory]
    [InlineData("module")]
    [InlineData("capability")]
    [InlineData("accounts")]
    [InlineData("disabled")]
    [InlineData("read-only")]
    [InlineData("currency")]
    [InlineData("zone")]
    [InlineData("tenant")]
    [InlineData("role")]
    [InlineData("deleted")]
    public async Task Current_policy_tenant_zone_currency_and_database_customer_are_required_to_apply(string mode)
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        await using var db = DatabaseFixture.CreateContext();
        if (mode == "module") _modules.Setup(value => value.IsEnabled(ModuleIds.Reservations)).Returns(false);
        else if (mode == "capability") Mobile.EnabledCapabilities = ["accounts"];
        else if (mode == "accounts") Mobile.EnabledCapabilities = ["reservations"];
        else if (mode == "disabled") Mobile.Enabled = false;
        else if (mode == "read-only") Mobile.Lifecycle = mode;
        else if (mode == "currency") await db.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(row => row.Currency, (string?)null));
        else if (mode == "zone") _clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));
        else if (mode == "tenant") Mobile.TenantId = Guid.NewGuid();
        else if (mode == "role") await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, UserRole.Admin));
        else await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("submit", proof)).StatusCode);
        await AssertUntouchedAsync();
        if (mode is not ("tenant" or "role" or "deleted"))
        {
            Assert.Equal("Unseen", (await DecideAsync("recover", proof)).GetProperty("outcome").GetString());
            Assert.Equal("Cancelled", (await DecideAsync("cancel-attempt", proof)).GetProperty("outcome").GetString());
        }
    }
    [Theory]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.NoShow)]
    public async Task Terminal_bookings_cannot_prepare_cancellation(ReservationStatus status)
    {
        Authenticate(); await using var db = DatabaseFixture.CreateContext();
        await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, status));
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("prepare", Request())).StatusCode);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
    }
    [Theory]
    [InlineData("session")]
    [InlineData("customer")]
    [InlineData("operation")]
    [InlineData("tenant")]
    [InlineData("hash")]
    [InlineData("proof")]
    public async Task Original_proof_cannot_be_rebound_or_changed_before_an_effect_or_decision(string mode)
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        if (mode == "customer")
        {
            var other = Guid.NewGuid();
            await using var db = DatabaseFixture.CreateContext();
            db.Users.Add(new ApplicationUser
            {
                Id = other,
                UserName = "other-cancellation-customer",
                Email = "other@example.test",
                FirstName = "Other",
                LastName = "Customer",
                Role = UserRole.Customer,
                RefreshToken = "",
                CreatedBy = "test"
            });
            await db.SaveChangesAsync();
            Client.DefaultRequestHeaders.Authorization = new("Bearer", CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, userId: other.ToString("D")));
        }
        var invalid = mode switch
        {
            "operation" => proof with { OperationId = Guid.NewGuid() },
            "tenant" => proof with { TenantId = Guid.NewGuid() },
            "hash" => proof with { PayloadHash = new string(proof.PayloadHash[0] == 'a' ? 'b' : 'a', 64) },
            "proof" => proof with { OwnerProof = "unprotected" },
            _ => proof
        };
        foreach (var path in new[] { "submit", "recover", "cancel-attempt" })
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(path, invalid, mode == "session" ? Guid.NewGuid().ToString("D") : null)).StatusCode);
        await AssertUntouchedAsync();
    }
    [Theory]
    [InlineData("clock")]
    [InlineData("policy")]
    [InlineData("tenant")]
    public async Task Authority_is_rechecked_after_the_actual_status_and_receipt_save_before_commit(string mode)
    {
        Authenticate(); var proof = (await PrepareAsync()).Proof;
        var calls = 0;
        _afterAccepting = () =>
        {
            if (++calls != (mode == "clock" ? 2 : 1)) return;
            if (mode == "clock") _clock.SetupGet(value => value.Now).Returns(new DateTimeOffset(2030, 5, 17, 18, 0, 0, TimeSpan.FromHours(2)));
            else if (mode == "policy") Mobile.Enabled = false;
            else Mobile.TenantId = Guid.NewGuid();
        };
        var expected = mode == "clock" ? HttpStatusCode.Conflict : HttpStatusCode.NotFound;
        Assert.Equal(expected, (await SendAsync("submit", proof)).StatusCode);
        Assert.True(calls > 0); await AssertUntouchedAsync();
    }
}
