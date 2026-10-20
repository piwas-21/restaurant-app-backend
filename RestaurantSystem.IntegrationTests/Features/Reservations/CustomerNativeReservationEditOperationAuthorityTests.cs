using System.Net;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Features.Basket;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditOperationTests
{
    [Theory]
    [InlineData("anonymous")]
    [InlineData("expired")]
    [InlineData("admin")]
    [InlineData("cashier")]
    [InlineData("server")]
    public async Task All_four_edit_operations_require_valid_customer_authentication(string mode)
    {
        Authenticate(); var (request, review) = await PrepareAsync();
        if (mode == "anonymous") Client.DefaultRequestHeaders.Authorization = null;
        else Authenticate(mode switch { "admin" => UserRole.Admin, "cashier" => UserRole.Cashier, "server" => UserRole.Server, _ => UserRole.Customer }, mode == "expired");
        var expected = mode is "anonymous" or "expired" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await SendAsync("prepare", Request())).StatusCode);
        Assert.Equal(expected, (await SendAsync("submit", request)).StatusCode);
        foreach (var path in new[] { "recover", "cancel-attempt" }) Assert.Equal(expected, (await SendAsync(path, review.Proof)).StatusCode);
        await using var db = DatabaseFixture.CreateContext(); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(0, _mailCalls);
    }
    [Theory]
    [InlineData("module")]
    [InlineData("accounts")]
    [InlineData("reservations")]
    [InlineData("disabled")]
    [InlineData("read-only")]
    [InlineData("currency")]
    [InlineData("zone")]
    [InlineData("tenant")]
    [InlineData("role")]
    [InlineData("deleted")]
    public async Task Fresh_policy_and_customer_database_authority_are_required_but_proof_only_recovery_survives_module_withdrawal(string mode)
    {
        Authenticate(); var (request, review) = await PrepareAsync();
        await using var db = DatabaseFixture.CreateContext(); var revision = (await db.Reservations.SingleAsync()).Revision;
        if (mode == "module") _modules.Setup(value => value.IsEnabled(ModuleIds.Reservations)).Returns(false);
        else if (mode == "accounts") Mobile.EnabledCapabilities = ["reservations"];
        else if (mode == "reservations") Mobile.EnabledCapabilities = ["accounts"];
        else if (mode == "disabled") Mobile.Enabled = false;
        else if (mode == "read-only") Mobile.Lifecycle = mode;
        else if (mode == "currency") await db.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(row => row.Currency, "EUR"));
        else if (mode == "zone") _clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.Utc);
        else if (mode == "tenant") Mobile.TenantId = Guid.NewGuid();
        else if (mode == "role") await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, UserRole.Admin));
        else await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("submit", request)).StatusCode); await AssertUntouchedAsync(revision);
        if (mode is not ("tenant" or "role" or "deleted"))
        {
            Assert.Equal("Unseen", (await DecideAsync("recover", review.Proof)).GetProperty("outcome").GetString());
            Assert.Equal("Cancelled", (await DecideAsync("cancel-attempt", review.Proof)).GetProperty("outcome").GetString());
        }
    }
    [Theory]
    [InlineData("parent-aba")]
    [InlineData("child-aba")]
    [InlineData("same-values")]
    [InlineData("status")]
    public async Task Any_post_review_booking_revision_change_refuses_edit_without_a_decision(string mode)
    {
        Authenticate(); var (request, _) = await PrepareAsync(); await using var db = DatabaseFixture.CreateContext();
        if (mode == "child-aba")
        {
            var row = new ReservationTable { ReservationId = _booking, TableId = _spare, CreatedBy = "test" };
            db.Set<ReservationTable>().Add(row); await db.SaveChangesAsync(); db.Set<ReservationTable>().Remove(row); await db.SaveChangesAsync();
        }
        else if (mode == "status") await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ReservationStatus.Cancelled));
        else
        {
            await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerName, mode == "parent-aba" ? "Changed" : "Editor customer"));
            if (mode == "parent-aba") await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.CustomerName, "Editor customer"));
        }
        var revision = (await db.Reservations.AsNoTracking().SingleAsync()).Revision;
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync("submit", request)).StatusCode);
        Assert.Equal(revision, (await db.Reservations.AsNoTracking().SingleAsync()).Revision);
        Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
    [Theory]
    [InlineData("session")]
    [InlineData("operation")]
    [InlineData("tenant")]
    [InlineData("proof")]
    [InlineData("customer")]
    public async Task Original_edit_proof_cannot_be_rebound_before_submission_recovery_or_stop(string mode)
    {
        Authenticate(); var (request, review) = await PrepareAsync();
        if (mode == "customer")
        {
            var other = Guid.NewGuid(); await using var foreignDb = DatabaseFixture.CreateContext();
            foreignDb.Users.Add(new ApplicationUser
            {
                Id = other,
                UserName = "another-editor",
                Email = "another@example.test",
                FirstName = "Another",
                LastName = "Customer",
                Role = UserRole.Customer,
                RefreshToken = "",
                CreatedBy = "test"
            });
            await foreignDb.SaveChangesAsync();
            Client.DefaultRequestHeaders.Authorization = new("Bearer", CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, userId: other.ToString("D")));
        }
        var proof = mode switch
        {
            "operation" => review.Proof with { OperationId = Guid.NewGuid() },
            "tenant" => review.Proof with { TenantId = Guid.NewGuid() },
            "proof" => review.Proof with { OwnerProof = "invalid" },
            _ => review.Proof
        };
        var changed = request with { OperationId = proof.OperationId, TenantId = proof.TenantId, OwnerProof = proof.OwnerProof };
        var session = mode == "session" ? Guid.NewGuid().ToString("D") : null;
        foreach (var path in new[] { "recover", "cancel-attempt" }) Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(path, proof, session)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("submit", changed, session)).StatusCode);
        await using var db = DatabaseFixture.CreateContext(); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync()); Assert.Equal(0, _mailCalls);
    }
}
