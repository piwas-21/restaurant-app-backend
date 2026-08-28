using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.UpdateGlobalIngredientCommand;

/// <param name="IsActive">
/// Null means "the caller said nothing about availability", not "false" (#428). It is the whole
/// point of the nullable: the flag has no reverse gear in the UI, because nothing lists an inactive
/// row, so a partial payload must never be able to set it.
/// </param>
/// <param name="Kind">Null means "leave the classification alone" — same reasoning (#428).</param>
public record UpdateGlobalIngredientCommand(
    Guid Id,
    string DefaultName,
    string? ImageUrl,
    bool? IsActive,
    List<GlobalIngredientTranslationDto> Translations,
    // S5. Last and defaulted so every existing caller keeps its meaning; null now means "unchanged".
    IngredientKind? Kind = null
) : ICommand<ApiResponse<GlobalIngredientDto>>;

public class UpdateGlobalIngredientCommandHandler : ICommandHandler<UpdateGlobalIngredientCommand, ApiResponse<GlobalIngredientDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateGlobalIngredientCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<GlobalIngredientDto>> Handle(UpdateGlobalIngredientCommand command, CancellationToken cancellationToken)
    {
        var ingredient = await _context.GlobalIngredients
            .Include(g => g.Translations)
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);

        if (ingredient == null)
        {
            return ApiResponse<GlobalIngredientDto>.Failure("Global ingredient not found");
        }

        ingredient.DefaultName = command.DefaultName;
        ingredient.ImageUrl = command.ImageUrl;
        ingredient.IsActive = command.IsActive ?? ingredient.IsActive;
        ingredient.Kind = command.Kind ?? ingredient.Kind;

        SyncTranslations(ingredient, command.Translations, _currentUserService.GetAuditIdentifier());

        await _context.SaveChangesAsync(cancellationToken);

        var usedOnProductCount = await GlobalIngredientUsage.CountForAsync(_context, ingredient.Id, cancellationToken);

        return ApiResponse<GlobalIngredientDto>.SuccessWithData(
            GlobalIngredientMapper.ToDto(ingredient, usedOnProductCount));
    }

    private void SyncTranslations(GlobalIngredient ingredient, List<GlobalIngredientTranslationDto> incoming, string auditId)
    {
        var incomingCodes = incoming.Select(t => t.LanguageCode).ToHashSet();

        var toRemove = ingredient.Translations
            .Where(t => !incomingCodes.Contains(t.LanguageCode))
            .ToList();
        foreach (var translation in toRemove)
        {
            _context.GlobalIngredientTranslations.Remove(translation);
        }

        foreach (var dto in incoming)
        {
            var existing = ingredient.Translations.FirstOrDefault(t => t.LanguageCode == dto.LanguageCode);
            if (existing != null)
            {
                existing.Name = dto.Name;
            }
            else
            {
                ingredient.Translations.Add(new GlobalIngredientTranslation
                {
                    LanguageCode = dto.LanguageCode,
                    Name = dto.Name,
                    CreatedBy = auditId,
                });
            }
        }
    }
}
