using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientByIdQuery;

public record GetGlobalIngredientByIdQuery(Guid Id) : IQuery<ApiResponse<GlobalIngredientDto>>;

public class GetGlobalIngredientByIdQueryHandler : IQueryHandler<GetGlobalIngredientByIdQuery, ApiResponse<GlobalIngredientDto>>
{
    private readonly ApplicationDbContext _context;

    public GetGlobalIngredientByIdQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<GlobalIngredientDto>> Handle(GetGlobalIngredientByIdQuery query, CancellationToken cancellationToken)
    {
        var ingredient = await _context.GlobalIngredients
            .Include(g => g.Translations)
            .FirstOrDefaultAsync(g => g.Id == query.Id, cancellationToken);

        if (ingredient == null)
        {
            return ApiResponse<GlobalIngredientDto>.Failure("Global ingredient not found");
        }

        return ApiResponse<GlobalIngredientDto>.SuccessWithData(GlobalIngredientMapper.ToDto(ingredient));
    }
}
