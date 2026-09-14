using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>Contract tests for the additive printer-feed update projection. The legacy order page
/// remains owned by PrinterFeedContractSnapshotTests; these tests assert the new envelope member and
/// its device-only Kitchen audience.</summary>
[Collection("Database Lane 2")]
public class PrinterFeedUpdateTests : IntegrationTestBase
{
    private Guid _confirmedOrderId;
    private Guid _preparingOrderId;
    private Guid _pendingOrderId;
    private Guid _cancelledOrderId;

    public PrinterFeedUpdateTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public void Device_update_page_size_is_registered_with_the_default()
    {
        Factory.Services.GetRequiredService<IOptions<PrinterFeedSettings>>()
            .Value.UpdatePageSize.Should().Be(50);
    }

    [Fact]
    public async Task Device_feed_projects_kitchen_notes_with_stable_update_identity_only()
    {
        await CreateNote(_confirmedOrderId, "Kitchen instruction", "Kitchen");
        await CreateNote(_confirmedOrderId, "Private staff detail", "Staff");

        var feed = await FetchFeedAsync();
        var update = feed["data"]!["updates"]!.AsArray().Single()!;

        update["jobId"]!.GetValue<Guid>().Should().NotBeEmpty();
        update["revision"]!.GetValue<int>().Should().Be(1);
        update["jobType"]!.GetValue<string>().Should().Be(nameof(DevicePrintJobType.Update));
        update["target"]!.GetValue<string>().Should().Be(nameof(DevicePrintTarget.General));
        update["audience"]!.GetValue<string>().Should().Be(nameof(OrderNoteAudience.Kitchen));
        update["text"]!.GetValue<string>().Should().Be("Kitchen instruction");
        update["orderId"]!.GetValue<Guid>().Should().Be(_confirmedOrderId);
        update["orderNumber"]!.GetValue<string>().Should().Be("PF-UPDATE-CONFIRMED");
    }

    [Fact]
    public async Task Device_feed_uses_cursor_for_notes_saved_after_the_order()
    {
        await CreateNote(_confirmedOrderId, "Saved after order", "Kitchen");

        var cursor = DateTime.UtcNow.AddMinutes(-1).ToString("O");
        var feed = await FetchFeedAsync(cursor);
        feed["data"]!["updates"]!.AsArray()
            .Select(update => update!["text"]!.GetValue<string>())
            .Should().Contain("Saved after order");
    }

    [Fact]
    public async Task Device_feed_uses_opaque_composite_cursor_for_equal_timestamps_and_bounded_pages()
    {
        var createdAt = DateTime.UtcNow.AddMinutes(-1);
        await CreateNotesAtAsync(_confirmedOrderId, 51, createdAt);

        var firstPage = await FetchFeedAsync(
            modifiedSince: createdAt.AddSeconds(-1).ToString("O"));
        var firstUpdates = firstPage["data"]!["updates"]!.AsArray();
        var nextCursor = firstPage["data"]!["nextUpdateCursor"]!.GetValue<string>();

        firstUpdates.Should().HaveCount(50);
        nextCursor.Should().NotBeNullOrWhiteSpace();
        firstPage["data"]!["hasMoreUpdates"]!.GetValue<bool>().Should().BeTrue();

        var secondPage = await FetchFeedAsync(updateCursor: nextCursor);
        var secondUpdates = secondPage["data"]!["updates"]!.AsArray();
        var terminalCursor = secondPage["data"]!["nextUpdateCursor"]!.GetValue<string>();
        var allJobIds = firstUpdates
            .Concat(secondUpdates)
            .Select(update => update!["jobId"]!.GetValue<Guid>())
            .ToArray();

        secondUpdates.Should().HaveCount(1);
        terminalCursor.Should().NotBe(nextCursor);
        secondPage["data"]!["hasMoreUpdates"]!.GetValue<bool>().Should().BeFalse();
        allJobIds.Should().HaveCount(51);
        allJobIds.Should().OnlyHaveUniqueItems();

        var emptyPage = await FetchFeedAsync(updateCursor: terminalCursor);
        emptyPage["data"]!["updates"]!.AsArray().Should().BeEmpty();
        emptyPage["data"]!["nextUpdateCursor"]!.GetValue<string>().Should().Be(terminalCursor);
        emptyPage["data"]!["hasMoreUpdates"]!.GetValue<bool>().Should().BeFalse();

        await CreateNote(_confirmedOrderId, "Later kitchen note", "Kitchen");
        var laterPage = await FetchFeedAsync(updateCursor: terminalCursor);
        laterPage["data"]!["updates"]!.AsArray()
            .Select(update => update!["text"]!.GetValue<string>())
            .Should().ContainSingle("Later kitchen note");
        laterPage["data"]!["hasMoreUpdates"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Device_feed_does_not_project_staff_pending_or_cancelled_notes()
    {
        await CreateNote(_confirmedOrderId, "Confirmed kitchen note", "Kitchen");
        await CreateNote(_preparingOrderId, "Preparing kitchen update", "Kitchen");
        await CreateNote(_pendingOrderId, "Pending kitchen note", "Kitchen");
        await CreateNote(_cancelledOrderId, "Cancelled kitchen note", "Kitchen");

        var feed = await FetchFeedAsync();
        var texts = feed["data"]!["updates"]!.AsArray()
            .Select(update => update!["text"]!.GetValue<string>())
            .ToArray();

        texts.Should().Contain("Confirmed kitchen note");
        texts.Should().Contain("Preparing kitchen update");
        texts.Should().NotContain("Pending kitchen note");
        texts.Should().NotContain("Cancelled kitchen note");
    }

    [Fact]
    public async Task Device_feed_requires_the_printer_api_key_for_update_projection()
    {
        AuthenticateAsAnonymous();
        var response = await Client.GetAsync("/api/orders/printer-feed");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private async Task<JsonNode> FetchFeedAsync(
        string? modifiedSince = null,
        string? updateCursor = null)
    {
        AuthenticateAsDevice();
        var query = new List<string>();
        if (modifiedSince is not null)
        {
            query.Add($"modifiedSince={Uri.EscapeDataString(modifiedSince)}");
        }

        if (updateCursor is not null)
        {
            query.Add($"updateCursor={Uri.EscapeDataString(updateCursor)}");
        }

        var path = "/api/orders/printer-feed"
            + (query.Count > 0 ? $"?{string.Join("&", query)}" : string.Empty);
        var response = await Client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var feed = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        feed["success"]!.GetValue<bool>().Should().BeTrue();
        return feed;
    }

    private async Task CreateNotesAtAsync(Guid orderId, int count, DateTime createdAt)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.OrderOperationalNotes.AddRange(Enumerable.Range(0, count).Select(index =>
            new OrderOperationalNote
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Text = $"Equal timestamp note {index}",
                Audience = OrderNoteAudience.Kitchen,
                ClientOperationId = Guid.NewGuid(),
                CreatedAt = createdAt,
                CreatedBy = "test",
            }));
        await context.SaveChangesAsync();
    }

    private async Task CreateNote(Guid orderId, string text, string audience)
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync($"/api/orders/{orderId}/notes", new
        {
            text,
            audience,
            clientOperationId = Guid.NewGuid(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<OrderOperationalNoteDto>>(response);
        body!.Success.Should().BeTrue();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var confirmed = NewOrder("PF-UPDATE-CONFIRMED", OrderStatus.Confirmed);
        var preparing = NewOrder("PF-UPDATE-PREPARING", OrderStatus.Preparing);
        var pending = NewOrder("PF-UPDATE-PENDING", OrderStatus.Pending);
        var cancelled = NewOrder("PF-UPDATE-CANCELLED", OrderStatus.Cancelled);
        context.Orders.AddRange(confirmed, preparing, pending, cancelled);
        await context.SaveChangesAsync();
        _confirmedOrderId = confirmed.Id;
        _preparingOrderId = preparing.Id;
        _pendingOrderId = pending.Id;
        _cancelledOrderId = cancelled.Id;
    }

    private static Order NewOrder(string orderNumber, OrderStatus status) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = orderNumber,
        Type = OrderType.Takeaway,
        Status = status,
        PaymentStatus = PaymentStatus.Pending,
        Total = 10m,
        OrderDate = DateTime.UtcNow,
        CreatedBy = "test",
    };
}
