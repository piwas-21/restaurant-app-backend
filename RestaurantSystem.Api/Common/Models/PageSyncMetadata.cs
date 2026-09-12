namespace RestaurantSystem.Api.Common.Models;

/// <summary>Server-owned continuation state for a lossless paged read.</summary>
public sealed record PageSyncMetadata(
    string Mode,
    string? NextCursor,
    string? Watermark,
    bool HasMore,
    List<PageSyncRemoval> Removals);

/// <summary>An order that left the operational result while a change walk was in progress.</summary>
public sealed record PageSyncRemoval(Guid OrderId, string Reason);
