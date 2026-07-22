using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Settings.FormFields;
using RestaurantSystem.Api.Features.Settings.FormFields.Dtos;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Api.Features.Settings.FormFields.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Settings;

public class FormFieldConfigurationTests : IntegrationTestBase
{
    private const string Endpoint = "/api/FormFieldConfiguration";

    public FormFieldConfigurationTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private static object FieldChange(string formKey, string fieldKey, bool isVisible, bool isRequired) =>
        new { formKey, fieldKey, isVisible, isRequired };

    private async Task<List<FormFieldsDto>> GetFormsAsync()
    {
        var response = await Client.GetAsync(Endpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<FormFieldsDto>>>(JsonOptions);
        body!.Success.Should().BeTrue();
        return body.Data!;
    }

    // ---- GET ----------------------------------------------------------------

    [Fact]
    public async Task Get_Anonymous_ReturnsSeededDefaultsWithLockedFlags()
    {
        var forms = await GetFormsAsync();

        forms.Should().HaveCount(3);
        forms.Select(f => f.FormKey).Should().BeEquivalentTo(
            ["reservation", "checkout_contact", "delivery_address"]);

        var reservation = forms.Single(f => f.FormKey == "reservation");
        reservation.Fields.Select(f => f.FieldKey).Should().ContainInOrder(
            "customerName", "customerEmail", "customerPhone", "specialRequests");
        reservation.Fields.Single(f => f.FieldKey == "customerName")
            .Should().BeEquivalentTo(new { IsLocked = true, IsVisible = true, IsRequired = true });
        reservation.Fields.Single(f => f.FieldKey == "customerPhone")
            .Should().BeEquivalentTo(new { IsLocked = false, IsVisible = true, IsRequired = false });

        // Every registry row is lazily seeded on first read.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.CustomerFormFieldConfigurations.CountAsync())
            .Should().Be(FormFieldRegistry.Fields.Count);
    }

    [Fact]
    public async Task EnsureSeeded_RepeatedCallsAndFreshAppInstance_AreIdempotent()
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFormFieldConfigurationService>();
        await service.EnsureSeededAsync();
        await service.EnsureSeededAsync(); // seed-state short-circuit — no throw

        // A fresh seed-state mimics a new app replica hitting an already-seeded DB:
        // the key scan must no-op instead of violating the unique index.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var freshInstance = new FormFieldConfigurationService(db, currentUser, new FormFieldSeedState());
        await freshInstance.EnsureSeededAsync();

        (await db.CustomerFormFieldConfigurations.CountAsync())
            .Should().Be(FormFieldRegistry.Fields.Count);
    }

    // ---- PUT ----------------------------------------------------------------

    [Fact]
    public async Task Put_AsAdmin_RoundTripsAConfigurableChange()
    {
        AuthenticateAsAdmin();
        var response = await PutAsJsonAsync(Endpoint, new
        {
            fields = new[] { FieldChange("reservation", "customerPhone", isVisible: true, isRequired: true) }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<FormFieldsDto>>>(JsonOptions);
        body!.Success.Should().BeTrue();

        AuthenticateAsUser();
        var reservation = (await GetFormsAsync()).Single(f => f.FormKey == "reservation");
        reservation.Fields.Single(f => f.FieldKey == "customerPhone")
            .Should().BeEquivalentTo(new { IsLocked = false, IsVisible = true, IsRequired = true });
    }

    [Fact]
    public async Task Put_UnknownField_IsRejected()
    {
        AuthenticateAsAdmin();
        var response = await PutAsJsonAsync(Endpoint, new
        {
            fields = new[] { FieldChange("reservation", "shoeSize", isVisible: true, isRequired: false) }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unknown form field");
    }

    [Fact]
    public async Task Put_LockedFieldChange_IsRejected()
    {
        AuthenticateAsAdmin();
        var response = await PutAsJsonAsync(Endpoint, new
        {
            fields = new[] { FieldChange("reservation", "customerName", isVisible: true, isRequired: false) }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("locked");
    }

    [Fact]
    public async Task Put_RequiredButHidden_IsRejected()
    {
        AuthenticateAsAdmin();
        var response = await PutAsJsonAsync(Endpoint, new
        {
            fields = new[] { FieldChange("reservation", "customerPhone", isVisible: false, isRequired: true) }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("required while hidden");
    }

    [Fact]
    public async Task Put_WithoutAdmin_IsRejected()
    {
        var response = await PutAsJsonAsync(Endpoint, new
        {
            fields = new[] { FieldChange("reservation", "customerPhone", isVisible: true, isRequired: true) }
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // ---- Reservation enforcement -------------------------------------------

    private async Task<Guid> SeedTableAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var table = new Table { TableNumber = "T-FF", MaxGuests = 4, CreatedBy = "test" };
        db.Tables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private static object ReservationBody(Guid tableId, string? phone) => new
    {
        customerName = "Ada Lovelace",
        customerEmail = "ada@example.com",
        customerPhone = phone,
        tableId,
        reservationDate = DateTime.UtcNow.Date.AddDays(3),
        startTime = "18:00:00",
        endTime = "20:00:00",
        numberOfGuests = 2
    };

    private async Task MakePhoneRequiredAsync()
    {
        AuthenticateAsAdmin();
        var response = await PutAsJsonAsync(Endpoint, new
        {
            fields = new[] { FieldChange("reservation", "customerPhone", isVisible: true, isRequired: true) }
        });
        response.EnsureSuccessStatusCode();
        AuthenticateAsUser();
    }

    [Fact]
    public async Task CreateReservation_PhoneConfiguredRequiredAndMissing_Returns400NamingTheField()
    {
        var tableId = await SeedTableAsync();
        await MakePhoneRequiredAsync();

        var response = await PostAsJsonAsync("/api/Reservations", ReservationBody(tableId, phone: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("customerPhone");
    }

    [Fact]
    public async Task CreateReservation_PhoneConfiguredRequiredAndProvided_Succeeds()
    {
        var tableId = await SeedTableAsync();
        await MakePhoneRequiredAsync();

        var response = await PostAsJsonAsync("/api/Reservations", ReservationBody(tableId, phone: "+41791234567"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"success\":true");
    }

    [Fact]
    public async Task CreateReservation_DefaultConfig_EmptyPhoneStillSucceeds()
    {
        var tableId = await SeedTableAsync();

        var response = await PostAsJsonAsync("/api/Reservations", ReservationBody(tableId, phone: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"success\":true");
    }
}
