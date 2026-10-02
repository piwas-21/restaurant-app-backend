using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Additive printer work for an immutable Kitchen note or frozen amendment delta.
/// The note id is the stable job id; revision 1 identifies that job. Staff notes never enter this feed.</summary>
public record PrinterFeedUpdateDto
{
    public Guid JobId { get; init; }
    public int Revision { get; init; }
    public DevicePrintJobType JobType { get; init; } = DevicePrintJobType.Update;
    /// <summary>Logical kitchen destination. Legacy text notes use General; typed deltas carry
    /// their station target. The printer resolves that logical target to a configured destination.</summary>
    public DevicePrintTarget Target { get; init; } = DevicePrintTarget.General;
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public Guid? TableId { get; init; }
    public string? TableLabel { get; init; }
    public int? TableNumber { get; init; }
    public Guid? ServiceSessionId { get; init; }
    public Guid? AmendmentId { get; init; }
    public long? AccountRevision { get; init; }
    public IReadOnlyList<PrinterFeedChangeDto> Changes { get; init; } = [];
    public string Audience { get; init; } = nameof(OrderNoteAudience.Kitchen);
    public string Text { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
