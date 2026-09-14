using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Additive printer-feed work for a Kitchen-audience operational note. The immutable note id
/// is the stable job id and revision 1 identifies that job. Future editable-note work can add later
/// revisions without changing this contract. Staff notes never become this DTO.</summary>
public record PrinterFeedUpdateDto
{
    public Guid JobId { get; init; }
    public int Revision { get; init; }
    public DevicePrintJobType JobType { get; init; } = DevicePrintJobType.Update;
    /// <summary>Logical kitchen destination. The backend emits General; the printer app resolves it
    /// to its configured General/Default physical destination without creating two jobs.</summary>
    public DevicePrintTarget Target { get; init; } = DevicePrintTarget.General;
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public int? TableNumber { get; init; }
    public string Audience { get; init; } = nameof(OrderNoteAudience.Kitchen);
    public string Text { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
