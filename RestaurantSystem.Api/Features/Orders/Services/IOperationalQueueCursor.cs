using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOperationalQueueCursor
{
    string Protect(OperationalQueueCursorRequest request);
    OperationalQueueCursorPayload Read(string? value);
}

/// <summary>Server-owned fields used to issue an operational queue cursor.</summary>
public sealed record OperationalQueueCursorRequest
{
    public required string Mode { get; init; }
    public required string FilterHash { get; init; }
    public long UpperSequence { get; init; }
    public long LowerSequence { get; init; }
    public string? Position { get; init; }
    public Guid? PositionId { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
}
