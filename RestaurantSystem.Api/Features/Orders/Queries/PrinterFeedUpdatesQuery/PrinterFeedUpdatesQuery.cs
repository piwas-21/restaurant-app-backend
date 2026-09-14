using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery;

/// <summary>Reads additive printer jobs without changing the legacy order-feed query or its page.
/// <c>ModifiedSince</c> is only the initial boundary. Once a page is returned, clients advance with
/// the opaque timestamp/id <c>UpdateCursor</c> so bounded pages cannot lose equal-timestamp notes.
/// </summary>
public record PrinterFeedUpdatesQuery(DateTime? ModifiedSince, string? UpdateCursor = null)
    : IQuery<PrinterFeedUpdatesResult>;

public record PrinterFeedUpdatesResult(
    IReadOnlyList<PrinterFeedUpdateDto> Items,
    string? NextUpdateCursor,
    bool HasMoreUpdates);

public class PrinterFeedUpdatesQueryHandler
    : IQueryHandler<PrinterFeedUpdatesQuery, PrinterFeedUpdatesResult>
{
    private readonly ApplicationDbContext _context;
    private readonly int _updatePageSize;

    public PrinterFeedUpdatesQueryHandler(
        ApplicationDbContext context,
        IOptions<PrinterFeedSettings> printerFeedSettings)
    {
        _context = context;
        _updatePageSize = printerFeedSettings.Value.UpdatePageSize;
    }

    public async Task<PrinterFeedUpdatesResult> Handle(
        PrinterFeedUpdatesQuery query, CancellationToken cancellationToken)
    {
        var updatesQuery = _context.OrderOperationalNotes
            .AsNoTracking()
            // Device/API-key calls have no user role. This explicit audience predicate is the
            // printer boundary: Staff notes can never reach a printer through role inference.
            .Where(note => note.Audience == OrderNoteAudience.Kitchen
                && !note.Order.IsDeleted
                // A note may be added after the original ticket while preparation is in flight.
                // Exclude unreleased/terminal work, but keep the released kitchen states.
                && (note.Order.Status == OrderStatus.Confirmed
                    || note.Order.Status == OrderStatus.Preparing
                    || note.Order.Status == OrderStatus.Ready));

        var cursor = DecodeCursor(query.UpdateCursor);
        if (cursor.HasValue)
        {
            var value = cursor.Value;
            updatesQuery = updatesQuery.Where(note =>
                note.CreatedAt > value.CreatedAt
                || (note.CreatedAt == value.CreatedAt && note.Id.CompareTo(value.JobId) > 0));
        }
        else
        {
            var modifiedSinceUtc = QueryInstant.AsUtc(query.ModifiedSince);
            if (modifiedSinceUtc.HasValue)
            {
                updatesQuery = updatesQuery.Where(note => note.CreatedAt > modifiedSinceUtc.Value);
            }
        }

        var updates = await updatesQuery
            .OrderBy(note => note.CreatedAt)
            .ThenBy(note => note.Id)
            // Read one sentinel row so the response can tell the printer-app whether another page
            // exists without making it advance a timestamp cursor past unseen work.
            .Take(_updatePageSize + 1)
            .Select(note => new PrinterFeedUpdateDto
            {
                JobId = note.Id,
                // Each immutable note is one update job. A future editable-note contract can
                // introduce later revisions without changing the current retry identity.
                Revision = 1,
                JobType = DevicePrintJobType.Update,
                Target = DevicePrintTarget.General,
                OrderId = note.OrderId,
                OrderNumber = note.Order.OrderNumber,
                TableNumber = note.Order.TableNumber,
                Audience = nameof(OrderNoteAudience.Kitchen),
                Text = note.Text,
                CreatedAt = note.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var hasMore = updates.Count > _updatePageSize;
        if (hasMore)
        {
            updates.RemoveAt(_updatePageSize);
        }

        // Keep the last delivered position even when this is the terminal page. An empty poll
        // echoes its valid incoming cursor so clients never fall back to the time-only boundary.
        var nextCursor = GetNextCursor(updates, cursor.HasValue, query.UpdateCursor);
        return new PrinterFeedUpdatesResult(updates, nextCursor, hasMore);
    }

    private static string? GetNextCursor(
        List<PrinterFeedUpdateDto> updates, bool hasIncomingCursor, string? incomingCursor)
    {
        if (updates.Count > 0)
        {
            return PrinterFeedUpdateCursor.Encode(updates[^1].CreatedAt, updates[^1].JobId);
        }

        return hasIncomingCursor ? incomingCursor : null;
    }

    private static (DateTime CreatedAt, Guid JobId)? DecodeCursor(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : PrinterFeedUpdateCursor.Decode(value);
    }
}
