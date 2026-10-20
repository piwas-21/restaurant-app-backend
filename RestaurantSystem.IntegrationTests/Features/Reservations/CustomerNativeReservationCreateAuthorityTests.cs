using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCreateTests
{
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    public async Task Staff_cannot_create_or_seal_guest_bookings_through_the_native_route(UserRole role)
    {
        Authenticate(role);
        foreach (var cancel in new[] { false, true })
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(Request(), cancel)).StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal("Applied", (await ResolveAsync(Request())).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Expired_authentication_never_falls_back_to_anonymous_creation_or_sealing()
    {
        Authenticate(expired: true);
        foreach (var cancel in new[] { false, true })
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(Request(), cancel)).StatusCode);
        await using var db = DatabaseFixture.CreateContext();
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("disabled")]
    [InlineData("read-only")]
    [InlineData("module")]
    [InlineData("currency")]
    [InlineData("account")]
    [InlineData("role")]
    [InlineData("accounts-capability")]
    public async Task Fresh_native_and_database_customer_authority_are_required_before_creating(string mode)
    {
        Authenticate(); var request = Request();
        await PrepareAsync(request);
        await using var db = DatabaseFixture.CreateContext();
        if (mode == "capability") Mobile.EnabledCapabilities = ["accounts"];
        else if (mode == "accounts-capability") Mobile.EnabledCapabilities = ["reservations"];
        else if (mode == "module") _modules.Setup(value => value.IsEnabled(ModuleIds.Reservations)).Returns(false);
        else if (mode == "currency") await db.RestaurantInfo.ExecuteUpdateAsync(update => update.SetProperty(row => row.Currency, (string?)null));
        else if (mode == "account") await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        else if (mode == "role") await db.Users.Where(row => row.Id == Owner).ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, UserRole.Admin));
        else Mobile.Lifecycle = mode;
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(request)).StatusCode);
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(0, _mailCalls);
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("tenant")]
    [InlineData("timezone")]
    public async Task Authority_withdrawn_after_real_slot_query_rolls_back_staged_booking_and_receipt(string mode)
    {
        var observed = false;
        _afterSlots = () =>
        {
            observed = true;
            if (mode == "capability") Mobile.EnabledCapabilities = [];
            else if (mode == "tenant") Mobile.TenantId = Guid.NewGuid();
            else _clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));
        };
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(Request())).StatusCode); Assert.True(observed);
        await using var db = DatabaseFixture.CreateContext();
        Assert.Empty(await db.Reservations.ToListAsync()); Assert.Empty(await db.Set<CustomerReservationOperation>().ToListAsync());
        Assert.Equal(0, _mailCalls);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("party")]
    [InlineData("email")]
    [InlineData("name")]
    [InlineData("time")]
    [InlineData("date")]
    [InlineData("operation")]
    public async Task Invalid_create_payloads_cannot_write_or_leave_receipts(string mode)
    {
        var valid = Request();
        var invalid = mode switch
        {
            "schema" => valid with { SchemaVersion = null },
            "party" => valid with { NumberOfGuests = 0 },
            "email" => valid with { CustomerEmail = "invalid" },
            "name" => valid with { CustomerName = new string('a', 101) },
            "time" => valid with { EndTime = valid.StartTime },
            "date" => valid with { Date = null },
            _ => valid with { OperationId = Guid.Empty }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(invalid)).StatusCode);
        Assert.Equal("Applied", (await ResolveAsync(valid)).GetProperty("outcome").GetString());
        await using var db = DatabaseFixture.CreateContext(); Assert.Single(await db.Set<CustomerReservationOperation>().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_saved_create_decision_cannot_transfer_between_guest_and_customer_principals(bool originallyCustomer)
    {
        if (originallyCustomer) Authenticate();
        var request = Request(); var accepted = await ResolveAsync(request);
        if (originallyCustomer) Client.DefaultRequestHeaders.Authorization = null;
        else Authenticate();
        foreach (var cancel in new[] { false, true })
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(request, cancel)).StatusCode);
        if (originallyCustomer) Authenticate();
        else Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(accepted.GetRawText(), (await ResolveAsync(request)).GetRawText());
        Assert.Equal(1, _mailCalls);
    }

    [Fact]
    public async Task Unknown_staff_fields_are_not_silently_accepted()
    {
        var original = Request(); var proof = await PrepareAsync(original);
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Add("X-Session-Id", _session);
        request.Content = JsonContent.Create(new
        {
            schemaVersion = 1,
            operationId = original.OperationId,
            tenantId = Tenant,
            timeZone = "Europe/Zurich",
            date = "2030-05-17",
            startTime = "18:00:00",
            endTime = "20:00:00",
            numberOfGuests = 6,
            customerName = "Native guest",
            customerEmail = "guest@example.test",
            customerPhone = original.CustomerPhone,
            specialRequests = original.SpecialRequests,
            payloadHash = proof.PayloadHash,
            ownerProof = proof.OwnerProof,
            status = "Confirmed",
            tableId = _first
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.SendAsync(request)).StatusCode);
        Assert.Equal("Applied", (await ResolveAsync(original)).GetProperty("outcome").GetString());
    }
}
