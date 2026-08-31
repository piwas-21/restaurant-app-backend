using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Queries.GetLandingPageQuery;

/// <summary>Returns only the public landing-page configuration.</summary>
public record GetLandingPageQuery : IQuery<ApiResponse<LandingPageDto>>;

public class GetLandingPageQueryHandler
    : IQueryHandler<GetLandingPageQuery, ApiResponse<LandingPageDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public GetLandingPageQueryHandler(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<ApiResponse<LandingPageDto>> Handle(
        GetLandingPageQuery query, CancellationToken cancellationToken)
    {
        var info = await _context.RestaurantInfo
            .AsNoTracking()
            .Include(item => item.LandingContents)
            .FirstOrDefaultAsync(cancellationToken);

        if (info is null)
        {
            throw new NotFoundException("Restaurant info has not been initialised.");
        }

        return ApiResponse<LandingPageDto>.SuccessWithData(
            RestaurantLandingPageMapper.ToDto(info, _configuration["AWS:S3:BaseUrl"]));
    }
}
