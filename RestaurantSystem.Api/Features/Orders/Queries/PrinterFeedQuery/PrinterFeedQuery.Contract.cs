using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery;

/// <summary>
/// <paramref name="Language"/> asks for the order DETAILS (product, variation and ingredient
/// names) in a specific language — the print-language switch the printer-app sends on every poll
/// (2026-09-10 partner request; the ticket's labels were always translated, the names never were).
/// Values: a print-safe code (en/de/fr/it/es/nl/tr) resolves every name in that language with the
/// frozen checkout name as fallback; "auto" resolves per order from the order's own
/// PreferredLanguage within the same set; anything else (or absent) is today's behaviour — the
/// frozen single-language names. See <see cref="OrderDisplayTranslator"/>.
/// </summary>
public record PrinterFeedQuery(
    DateTime? ModifiedSince,
    string? Language = null,
    string? DeviceId = null,
    string? OrderCursor = null,
    DateTime? RequiredQueueRecoveryCutoff = null,
    int ProjectionVersion = 1) : IQuery<List<OrderDto>>
{
    public const int MaxOrdersPerPoll = 50;
}
