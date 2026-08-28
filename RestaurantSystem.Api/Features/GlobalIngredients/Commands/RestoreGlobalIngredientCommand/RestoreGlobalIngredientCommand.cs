using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.RestoreGlobalIngredientCommand;

public record RestoreGlobalIngredientCommand(Guid Id) : ICommand<ApiResponse<GlobalIngredientDto>>;

/// <summary>
/// Puts an archived library row back on the shelf (plan D4: archiving is reversible, which is the
/// property that makes it safe to offer at all).
///
/// <para>
/// It restores an ARCHIVED row and nothing else. A soft-deleted row is invisible to this lookup, so
/// it reports "not found" — undoing a delete would mean reading through the global query filter,
/// and the catalog only ever soft-deletes a row that no product uses, which is the case where
/// nothing is at stake. The endpoint that produced the two states is one <c>DELETE</c>, so the
/// asymmetry is documented in the picker by the button's own label.
/// </para>
/// </summary>
public class RestoreGlobalIngredientCommandHandler : ICommandHandler<RestoreGlobalIngredientCommand, ApiResponse<GlobalIngredientDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RestoreGlobalIngredientCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<GlobalIngredientDto>> Handle(RestoreGlobalIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = await _context.GlobalIngredients
            .Include(g => g.Translations)
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);

        if (ingredient == null)
        {
            return ApiResponse<GlobalIngredientDto>.Failure("Global ingredient not found");
        }

        if (!ingredient.ArchivedAt.HasValue)
        {
            return ApiResponse<GlobalIngredientDto>.Failure("Global ingredient is not archived");
        }

        ingredient.ArchivedAt = null;
        ingredient.ArchivedBy = null;
        ingredient.UpdatedAt = DateTime.UtcNow;
        ingredient.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        var usedOnProductCount = await GlobalIngredientUsage.CountForAsync(_context, ingredient.Id, cancellationToken);

        return ApiResponse<GlobalIngredientDto>.SuccessWithData(
            GlobalIngredientMapper.ToDto(ingredient, usedOnProductCount));
    }
}
