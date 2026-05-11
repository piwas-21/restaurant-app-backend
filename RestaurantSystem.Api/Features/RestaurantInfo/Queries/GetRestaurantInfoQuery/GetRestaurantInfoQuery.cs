using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Queries.GetRestaurantInfoQuery;

/// <summary>
/// Returns the singleton <see cref="Domain.Entities.RestaurantInfo"/> row plus
/// its phone numbers (active + inactive — clients filter as needed).
/// Public endpoint.
/// </summary>
public record GetRestaurantInfoQuery : IQuery<ApiResponse<RestaurantInfoDto>>;

public class GetRestaurantInfoQueryHandler
    : IQueryHandler<GetRestaurantInfoQuery, ApiResponse<RestaurantInfoDto>>
{
    private readonly ApplicationDbContext _context;

    public GetRestaurantInfoQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<RestaurantInfoDto>> Handle(
        GetRestaurantInfoQuery query, CancellationToken cancellationToken)
    {
        var info = await _context.RestaurantInfo
            .AsNoTracking()
            .Include(r => r.PhoneNumbers)
            .FirstOrDefaultAsync(cancellationToken);

        if (info is null)
        {
            // The migration seeds a singleton row, so this only fires if the
            // table was manually wiped — surface the misconfiguration.
            throw new NotFoundException("Restaurant info has not been initialised.");
        }

        var dto = new RestaurantInfoDto(
            info.Id,
            info.Name,
            info.AddressLine1,
            info.AddressLine2,
            info.City,
            info.PostalCode,
            info.Country,
            info.Latitude,
            info.Longitude,
            info.Email,
            info.Website,
            info.PhoneNumbers
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new RestaurantPhoneNumberDto(
                    p.Id, p.Label, p.Number, p.WhatsAppEnabled, p.DisplayOrder, p.IsActive))
                .ToList());

        return ApiResponse<RestaurantInfoDto>.SuccessWithData(dto);
    }
}
