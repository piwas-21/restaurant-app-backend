using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Devices.Queries.GetDevicesQuery;

/// <summary>Lists every known printer-app installation with its last-reported fleet status,
/// most-recently-seen first. Admin read.</summary>
public record GetDevicesQuery() : IQuery<ApiResponse<List<DeviceSummaryDto>>>;

public class GetDevicesQueryHandler
    : IQueryHandler<GetDevicesQuery, ApiResponse<List<DeviceSummaryDto>>>
{
    private readonly ApplicationDbContext _context;

    public GetDevicesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<ApiResponse<List<DeviceSummaryDto>>> Handle(
        GetDevicesQuery query, CancellationToken cancellationToken)
    {
        var devices = await _context.PrinterDevices
            .AsNoTracking()
            .OrderByDescending(d => d.LastHeartbeatAt)
            .Select(d => new DeviceSummaryDto(
                d.DeviceId,
                d.Label,
                d.TenantSlug,
                d.Platform,
                d.AppVersion,
                d.LastHeartbeatAt,
                d.FeedRunning,
                d.LastSuccessfulPollAt,
                d.ApiBaseUrl,
                d.KitchenPrinter,
                d.CashierPrinter,
                d.CreatedAt))
            .ToListAsync(cancellationToken);

        return ApiResponse<List<DeviceSummaryDto>>.SuccessWithData(devices);
    }
}
