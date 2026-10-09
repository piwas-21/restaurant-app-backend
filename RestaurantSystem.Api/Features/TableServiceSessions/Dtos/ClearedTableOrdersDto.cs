namespace RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

public sealed record ClearedTableOrdersDto(
    Guid? ServiceSessionId,
    int? TableNumber,
    int CancelledOrderCount,
    DateTime ClearedAt,
    DateTime? TableReleasedAt);
