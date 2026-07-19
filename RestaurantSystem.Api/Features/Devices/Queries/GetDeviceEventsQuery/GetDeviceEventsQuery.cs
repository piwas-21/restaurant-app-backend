using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Devices.Queries.GetDeviceEventsQuery;

/// <summary>Recent diagnostic events for one device, newest-first. Admin read. <see cref="Limit"/>
/// is clamped to <see cref="MaxLimit"/>.</summary>
public record GetDeviceEventsQuery(string DeviceId, int Limit)
    : IQuery<ApiResponse<List<DeviceEventLogDto>>>
{
    public const int MaxLimit = 500;
}

public class GetDeviceEventsQueryHandler
    : IQueryHandler<GetDeviceEventsQuery, ApiResponse<List<DeviceEventLogDto>>>
{
    private readonly ApplicationDbContext _context;

    public GetDeviceEventsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<ApiResponse<List<DeviceEventLogDto>>> Handle(
        GetDeviceEventsQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Limit, 1, GetDeviceEventsQuery.MaxLimit);

        var events = await _context.DeviceEvents
            .AsNoTracking()
            .Where(e => e.DeviceId == query.DeviceId)
            .OrderByDescending(e => e.OccurredAt)
            .Take(take)
            .Select(e => new DeviceEventLogDto(
                e.Id,
                e.ClientEventId,
                e.OccurredAt,
                e.Level,
                e.Code,
                e.Message,
                e.Context,
                e.CreatedAt))
            .ToListAsync(cancellationToken);

        return ApiResponse<List<DeviceEventLogDto>>.SuccessWithData(events);
    }
}
