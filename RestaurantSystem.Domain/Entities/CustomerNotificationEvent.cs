namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Opaque refresh hint captured in the subject's transaction. EventId is the stable downstream
/// deduplication key; neither this record nor a notification constitutes current read authority.
/// </summary>
public sealed class CustomerNotificationEvent
{
    public Guid EventId { get; init; }
    public required string SubjectKind { get; init; }
    public Guid SubjectId { get; init; }
    public DateTime OccurredAt { get; init; }
}
