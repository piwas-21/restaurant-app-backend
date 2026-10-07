using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Queries.GetPrinterUpdateAuthorizationQuery;
using RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class PrinterUpdateWithdrawalTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime CreatedAt = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Authorization_uses_api_key_and_response_envelope()
    {
        var jobId = await SeedNoteAsync();
        var path = $"/api/printer-feed/updates/{jobId}/authorization?revision=1&target=General";
        using var denied = await Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        AuthenticateAsDevice();
        using var response = await Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.True(body["success"]!.GetValue<bool>());
        Assert.Equal(jobId, body["data"]!["jobId"]!.GetValue<Guid>());
        Assert.Equal(1, body["data"]!["revision"]!.GetValue<int>());
        Assert.Equal("General", body["data"]!["target"]!.GetValue<string>());
        Assert.Equal("Authorized", body["data"]!["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(0, DevicePrintTarget.General)]
    [InlineData(2, DevicePrintTarget.General)]
    [InlineData(99, DevicePrintTarget.General)]
    [InlineData(1, DevicePrintTarget.FrontKitchen)]
    public async Task Wrong_revision_or_target_cannot_authorize_cached_output(int revision, DevicePrintTarget target)
    {
        var jobId = await SeedNoteAsync();
        await using var context = DatabaseFixture.CreateContext();
        var result = await new GetPrinterUpdateAuthorizationQueryHandler(context).Handle(
            new GetPrinterUpdateAuthorizationQuery(jobId, revision, target), CancellationToken.None);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(revision, result.Revision);
        Assert.Equal(target.ToString(), result.Target);
    }

    [Fact]
    public async Task Withdrawal_advances_existing_cursor_and_revokes_both_revisions_even_on_hidden_order()
    {
        var jobId = await SeedNoteAsync();
        await using var context = DatabaseFixture.CreateContext();
        var query = new PrinterFeedUpdatesQueryHandler(context,
            Microsoft.Extensions.Options.Options.Create(new RestaurantSystem.Api.Settings.PrinterFeedSettings()));
        var original = await query.Handle(new PrinterFeedUpdatesQuery(null), CancellationToken.None);
        Assert.Equal(jobId, Assert.Single(original.Items).JobId);
        Assert.False(original.Items[0].IsWithdrawn);
        var withdrawalTime = CreatedAt.AddMinutes(5);
        var note = await context.OrderOperationalNotes.SingleAsync(value => value.Id == jobId);
        note.Text = "[erased]";
        note.WithdrawnAt = withdrawalTime;
        var order = await context.Orders.SingleAsync(value => value.Id == note.OrderId);
        order.IsDeleted = true;
        await context.SaveChangesAsync();

        var withdrawn = await query.Handle(new PrinterFeedUpdatesQuery(null, original.NextUpdateCursor),
            CancellationToken.None);
        var update = Assert.Single(withdrawn.Items);
        Assert.Equal(jobId, update.JobId);
        Assert.Equal(2, update.Revision);
        Assert.True(update.IsWithdrawn);
        Assert.Equal(withdrawalTime, update.CreatedAt);
        Assert.Empty(update.Changes);
        Assert.Empty(update.Text);
        var authority = new GetPrinterUpdateAuthorizationQueryHandler(context);
        foreach (var revision in new[] { 1, 2 })
            Assert.Equal("Withdrawn", (await authority.Handle(
                new GetPrinterUpdateAuthorizationQuery(jobId, revision, DevicePrintTarget.General),
                CancellationToken.None)).Status);
        Assert.Equal("Unavailable", (await authority.Handle(
            new GetPrinterUpdateAuthorizationQuery(jobId, 99, DevicePrintTarget.General),
            CancellationToken.None)).Status);
        var next = await query.Handle(new PrinterFeedUpdatesQuery(null, withdrawn.NextUpdateCursor),
            CancellationToken.None);
        Assert.Empty(next.Items);
    }

    [Fact]
    public async Task Staff_note_and_unknown_job_never_authorize_printing()
    {
        var jobId = await SeedNoteAsync(OrderNoteAudience.Staff);
        await using var context = DatabaseFixture.CreateContext();
        var handler = new GetPrinterUpdateAuthorizationQueryHandler(context);
        foreach (var id in new[] { jobId, Guid.NewGuid() })
            Assert.Equal("Unavailable", (await handler.Handle(
                new GetPrinterUpdateAuthorizationQuery(id, 1, DevicePrintTarget.General),
                CancellationToken.None)).Status);
    }

    private async Task<Guid> SeedNoteAsync(OrderNoteAudience audience = OrderNoteAudience.Kitchen)
    {
        await using var context = DatabaseFixture.CreateContext();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"WITHDRAW-{Guid.NewGuid():N}"[..20],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            Total = 12.35m,
            OrderDate = CreatedAt,
            CreatedAt = CreatedAt,
            CreatedBy = "withdrawal-test"
        };
        var note = new OrderOperationalNote
        {
            Id = Guid.NewGuid(),
            Order = order,
            OrderId = order.Id,
            Text = "Private kitchen instructions",
            Audience = audience,
            ClientOperationId = Guid.NewGuid(),
            CreatedAt = CreatedAt,
            CreatedBy = "withdrawal-test"
        };
        context.OrderOperationalNotes.Add(note);
        await context.SaveChangesAsync();
        return note.Id;
    }
}
