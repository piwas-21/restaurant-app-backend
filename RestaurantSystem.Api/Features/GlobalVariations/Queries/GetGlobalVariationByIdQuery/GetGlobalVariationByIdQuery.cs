using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Api.Features.GlobalVariations.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalVariations.Queries.GetGlobalVariationByIdQuery;

public record GetGlobalVariationByIdQuery(Guid Id) : IQuery<ApiResponse<GlobalVariationDto>>;

public class GetGlobalVariationByIdQueryHandler : IQueryHandler<GetGlobalVariationByIdQuery, ApiResponse<GlobalVariationDto>>
{
    private readonly ApplicationDbContext _context;

    public GetGlobalVariationByIdQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<GlobalVariationDto>> Handle(GetGlobalVariationByIdQuery query, CancellationToken cancellationToken)
    {
        var variation = await _context.GlobalVariations
            .Include(g => g.Translations)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == query.Id, cancellationToken);

        if (variation == null)
        {
            return ApiResponse<GlobalVariationDto>.Failure("Global variation not found");
        }

        var usedOnProductCount = await GlobalVariationUsage.CountForAsync(_context, variation.Id, cancellationToken);

        return ApiResponse<GlobalVariationDto>.SuccessWithData(
            GlobalVariationMapper.ToDto(variation, usedOnProductCount));
    }
}
