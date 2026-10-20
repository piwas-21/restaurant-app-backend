using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Json;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationCreateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Combined_creation_mail_uses_the_final_child_trigger_revision_and_its_actual_email_button_works(bool native)
    {
        Authenticate(); var observed = false;
        _onMail = async row =>
        {
            await using var independent = DatabaseFixture.CreateContext();
            var saved = await independent.Reservations.Include(value => value.CombinedTables).SingleAsync();
            Assert.Single(saved.CombinedTables); Assert.Equal(saved.Revision, row.Revision); observed = true;
        };
        Guid id;
        if (native) id = (await ResolveAsync(Request())).GetProperty("reservationId").GetGuid();
        else
        {
            using var response = await Client.PostAsJsonAsync("/api/Reservations", new CreateReservationDto
            {
                CustomerName = "Combined customer",
                CustomerEmail = "combined@example.test",
                CustomerPhone = "+41000000000",
                TableId = _first,
                CombinedTableIds = [_second],
                ReservationDate = Day,
                StartTime = TimeSpan.FromHours(18),
                EndTime = TimeSpan.FromHours(20),
                NumberOfGuests = 6
            });
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<ReservationDto>>(JsonOptions);
            Assert.NotNull(body); Assert.True(body.Success, body.Message); Assert.NotNull(body.Data); id = body.Data.Id;
        }
        Assert.True(observed);
        var admin = Assert.Single(_email.Invocations, value => value.Method.Name == nameof(IEmailService.SendEmailAsync));
        var match = Regex.Match((string)admin.Arguments[2], """quick-approve\?token=([^'"&<>\s]+)"""); Assert.True(match.Success);
        Client.DefaultRequestHeaders.Authorization = null;
        var html = await Client.GetStringAsync($"/api/reservations/{id}/quick-approve?token={Uri.EscapeDataString(Uri.UnescapeDataString(match.Groups[1].Value))}");
        Assert.Contains("Reservation Approved", html, StringComparison.Ordinal);
        await using var db = DatabaseFixture.CreateContext(); Assert.Equal(ReservationStatus.Confirmed, (await db.Reservations.SingleAsync()).Status);
        Assert.Equal(1, _mailCalls);
    }
}
