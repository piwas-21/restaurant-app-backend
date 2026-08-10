using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Composes the timeline entry a cancellation leaves behind, including the money it did NOT give
/// back.
/// </summary>
/// <remarks>
/// Its own type because <c>CancelOrderCommand</c> is a 200-line file and this is three concerns —
/// what to say, how much room there is to say it, and where a string may safely be cut. The same
/// call <c>MergedBasketChannelReset</c> came out of.
/// </remarks>
public static class CancellationNotes
{
    /// <summary>Length of <c>OrderStatusHistory.Notes</c> — see <c>OrderStatusHistoryConfiguration</c>.</summary>
    private const int NotesMaxLength = 500;

    private const string Prefix = "Cancellation reason: ";

    /// <summary>
    /// Builds the note, keeping the outstanding-refund warning whatever the reason is.
    /// </summary>
    /// <remarks>
    /// The warning is the part the system authors and the part a human must act on, so it is the
    /// part that survives: the staff-typed reason is trimmed around it. <c>Notes</c> is
    /// <c>varchar(500)</c> and <c>CancelOrderCommandValidator</c> puts <b>no</b> maximum on the
    /// reason, so a long enough one already threw on save and took the whole cancellation down
    /// (backend #340) — appending to it unguarded would only have made that likelier, and on the
    /// exact orders where the note matters most.
    /// </remarks>
    public static string Build(string reason, IReadOnlyCollection<OrderPayment> gatewayHeld)
    {
        var suffix = DescribeOutstandingRefunds(gatewayHeld);

        var room = Math.Max(0, NotesMaxLength - Prefix.Length - suffix.Length);
        var notes = Prefix + Truncate(reason, room) + suffix;

        // A second clamp rather than trusting the arithmetic: `room` goes to zero when the suffix
        // alone would fill the column, and this is a column write, not a display string.
        return Truncate(notes, NotesMaxLength);
    }

    /// <summary>
    /// Names the money the cancellation did NOT give back, on the order's own timeline.
    /// </summary>
    /// <remarks>
    /// The log line beside it reaches an operator reading logs; this reaches the staff member who
    /// opens the order tomorrow and asks why the diner was never refunded. Empty — not a
    /// reassuring "all refunded" — when there is nothing outstanding, so the note appears only when
    /// it carries information.
    /// </remarks>
    private static string DescribeOutstandingRefunds(IReadOnlyCollection<OrderPayment> gatewayHeld)
    {
        if (gatewayHeld.Count == 0)
        {
            return string.Empty;
        }

        var total = gatewayHeld.Sum(p => p.Amount);
        var gateways = string.Join(", ", gatewayHeld.Select(p => p.PaymentGateway).Distinct());

        return $" | NOT REFUNDED HERE: {total} captured by {gateways} — issue the refund from that dashboard.";
    }

    /// <summary>
    /// Cuts a string to <paramref name="maxLength"/> UTF-16 units without splitting a character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The surrogate check is the whole reason this is a method. A plain <c>value[..max]</c> can cut
    /// between the two halves of an astral character — an emoji in a cancellation reason is the
    /// ordinary case — leaving a lone high surrogate, and Npgsql's text converter uses a THROWING
    /// UTF-8 encoder: the save dies with <c>EncoderFallbackException</c> and the cancellation is
    /// lost. Truncating at all is new here, so this failure would have been new too, and it would
    /// have landed on exactly the orders that carry a gateway warning, since the warning is what
    /// makes the reason need trimming.
    /// </para>
    /// <para>
    /// Measuring in UTF-16 units against a <c>varchar(500)</c> that counts CODEPOINTS is deliberate
    /// and one-directional: an astral character is 2 units and 1 column character, so this
    /// over-trims rather than under-trims. Safe for the column; never the reverse.
    /// </para>
    /// </remarks>
    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = maxLength;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return value[..cut];
    }
}
