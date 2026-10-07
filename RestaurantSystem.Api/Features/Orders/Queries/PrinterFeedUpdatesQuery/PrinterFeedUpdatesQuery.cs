using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
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
            // soft-delete-bypass: withdrawal tombstones must purge cached jobs for hidden orders.
            .IgnoreQueryFilters()
            .AsNoTracking()
            // Device/API-key calls have no user role. This explicit audience predicate is the
            // printer boundary: Staff notes can never reach a printer through role inference.
            .Where(note => note.Audience == OrderNoteAudience.Kitchen
                && (note.WithdrawnAt.HasValue || !note.Order.IsDeleted)
                // Ordinary notes follow the existing preparation states. Frozen amendment jobs
                // survive terminal status: an offline kitchen still needs the cancellation ticket.
                && (note.WithdrawnAt.HasValue || note.KitchenChangesJson != null
                    || note.Order.Status == OrderStatus.Confirmed
                    || note.Order.Status == OrderStatus.Preparing
                    || note.Order.Status == OrderStatus.Ready));

        var cursor = DecodeCursor(query.UpdateCursor);
        if (cursor.HasValue)
        {
            var value = cursor.Value;
            updatesQuery = updatesQuery.Where(note =>
                EF.Property<DateTime>(note, "FeedEventAt") > value.CreatedAt
                || (EF.Property<DateTime>(note, "FeedEventAt") == value.CreatedAt && note.Id.CompareTo(value.JobId) > 0));
        }
        else
        {
            var modifiedSinceUtc = QueryInstant.AsUtc(query.ModifiedSince);
            if (modifiedSinceUtc.HasValue)
            {
                updatesQuery = updatesQuery.Where(note => EF.Property<DateTime>(note, "FeedEventAt") > modifiedSinceUtc.Value);
            }
        }

        var storedUpdates = await updatesQuery
            .OrderBy(note => EF.Property<DateTime>(note, "FeedEventAt"))
            .ThenBy(note => note.Id)
            // Read one sentinel row so the response can tell the printer-app whether another page
            // exists without making it advance a timestamp cursor past unseen work.
            .Take(_updatePageSize + 1)
            .Select(note => new StoredKitchenUpdate(new PrinterFeedUpdateDto
            {
                JobId = note.Id,
                Revision = note.WithdrawnAt.HasValue
                    ? PrinterUpdateRevisions.Withdrawal : PrinterUpdateRevisions.Original,
                IsWithdrawn = note.WithdrawnAt.HasValue,
                JobType = DevicePrintJobType.Update,
                Target = note.KitchenTarget ?? DevicePrintTarget.General,
                OrderId = note.OrderId,
                OrderNumber = note.Order.OrderNumber,
                TableId = note.Order.TableId,
                TableLabel = note.Order.TableLabel,
                TableNumber = note.Order.TableNumber,
                ServiceSessionId = note.Order.ServiceSessionId,
                AmendmentId = note.AmendmentId,
                AccountRevision = note.AccountRevision,
                Audience = nameof(OrderNoteAudience.Kitchen),
                Text = note.WithdrawnAt.HasValue ? string.Empty : note.Text,
                CreatedAt = EF.Property<DateTime>(note, "FeedEventAt"),
            }, note.KitchenChangesJson))
            .ToListAsync(cancellationToken);
        var updates = storedUpdates.Select(stored => stored.Update with
        {
            Changes = stored.Update.IsWithdrawn ? [] : KitchenChangeSnapshot.Deserialize(stored.ChangesJson)
        }).ToList();

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

    private sealed record StoredKitchenUpdate(PrinterFeedUpdateDto Update, string? ChangesJson);
}
