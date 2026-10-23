namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Internal capture interest, never subscription authority. Only a future authorized subscription
/// writer may install it. Its presence alone grants no right to dispatch or read a subject.
/// </summary>
public sealed class CustomerNotificationWatch
{
    public required string SubjectKind { get; init; }
    public Guid SubjectId { get; init; }
}
