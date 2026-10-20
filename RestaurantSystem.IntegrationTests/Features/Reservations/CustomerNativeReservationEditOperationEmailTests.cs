using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

public sealed partial class CustomerNativeReservationEditOperationTests
{
    [Theory]
    [InlineData("shape")]
    [InlineData("contact")]
    [InlineData("no-op")]
    public async Task Actual_native_edits_never_revive_old_pending_email_authority_and_mail_the_committed_revision(string mode)
    {
        await using var db = DatabaseFixture.CreateContext();
        await db.Reservations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ReservationStatus.Pending));
        var before = await db.Reservations.AsNoTracking().SingleAsync();
        var links = Factory.Services.GetRequiredService<IReservationQuickActionLinks>();
        var approve = links.Mint(_booking, ReservationQuickAction.Approve, before.Status, before.Revision);
        var reject = links.Mint(_booking, ReservationQuickAction.Reject, before.Status, before.Revision);
        Client.DefaultRequestHeaders.Authorization = null;
        if (mode == "shape")
            Assert.Contains("Reservation Approved", await ActionAsync("approve", approve), StringComparison.Ordinal);
        Authenticate(); var observed = false;
        _onMail = async (row, _) =>
        {
            await using var independent = DatabaseFixture.CreateContext();
            var saved = await independent.Reservations.AsNoTracking().SingleAsync();
            Assert.NotEqual(before.Revision, saved.Revision); Assert.Equal(saved.Revision, row.Revision);
            Assert.Equal(ReservationStatus.Pending, saved.Status); observed = true;
        };
        var input = Request();
        if (mode != "shape") input = input with { StartTime = before.StartTime, EndTime = before.EndTime };
        if (mode == "no-op") input = input with
        {
            CustomerName = before.CustomerName,
            CustomerEmail = before.CustomerEmail,
            CustomerPhone = before.CustomerPhone,
            SpecialRequests = before.SpecialRequests
        };
        var (request, review) = await PrepareAsync(input);
        var resolution = await DecideAsync("submit", request);
        Assert.Equal("Applied", resolution.GetProperty("outcome").GetString()); Assert.True(observed);
        Assert.Equal(resolution.GetRawText(), (await DecideAsync("recover", review.Proof)).GetRawText());
        Assert.Equal(resolution.GetRawText(), (await DecideAsync("submit", request)).GetRawText());
        Assert.Equal(1, _mailCalls);
        var alert = Assert.Single(_email.Invocations, call => call.Method.Name == nameof(IEmailService.SendEmailAsync));
        var match = Regex.Match((string)alert.Arguments[2], """quick-approve\?token=([^'"&<>\s]+)"""); Assert.True(match.Success);
        Client.DefaultRequestHeaders.Authorization = null;
        foreach (var (action, token) in new[] { ("approve", approve), ("reject", reject) })
            Assert.Contains("This link can no longer be used", await ActionAsync(action, token), StringComparison.Ordinal);
        Assert.Equal(ReservationStatus.Pending, (await db.Reservations.AsNoTracking().SingleAsync()).Status);
        var fresh = Uri.UnescapeDataString(match.Groups[1].Value);
        Assert.Contains("Reservation Approved", await ActionAsync("approve", fresh), StringComparison.Ordinal);
        Assert.Contains("This link can no longer be used", await ActionAsync("approve", fresh), StringComparison.Ordinal);
        Assert.Equal(ReservationStatus.Confirmed, (await db.Reservations.AsNoTracking().SingleAsync()).Status);
    }
    private Task<string> ActionAsync(string action, string token) =>
        Client.GetStringAsync($"/api/reservations/{_booking}/quick-{action}?token={Uri.EscapeDataString(token)}");
}
