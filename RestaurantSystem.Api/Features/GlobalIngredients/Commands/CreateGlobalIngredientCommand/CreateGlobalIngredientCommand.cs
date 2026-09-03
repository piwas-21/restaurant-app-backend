using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.CreateGlobalIngredientCommand;

public record CreateGlobalIngredientCommand(
    string DefaultName,
    string? ImageUrl,
    List<GlobalIngredientTranslationDto> Translations,
    // S5. Last and defaulted so every existing caller keeps creating ingredients.
    IngredientKind Kind = IngredientKind.Ingredient
) : ICommand<ApiResponse<GlobalIngredientDto>>;

public class CreateGlobalIngredientCommandHandler : ICommandHandler<CreateGlobalIngredientCommand, ApiResponse<GlobalIngredientDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreateGlobalIngredientCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<GlobalIngredientDto>> Handle(CreateGlobalIngredientCommand command, CancellationToken cancellationToken)
    {
        var auditId = _currentUserService.GetAuditIdentifier();

        var ingredient = new GlobalIngredient
        {
            DefaultName = command.DefaultName,
            ImageUrl = command.ImageUrl,
            IsActive = true,
            Kind = command.Kind,
            // The one place a Custom ingredient is born — see LibraryOrigin for why the column
            // defaults to System instead.
            Origin = LibraryOrigin.Custom,
            CreatedBy = auditId,
            Translations = command.Translations
                .Select(t => new GlobalIngredientTranslation
                {
                    LanguageCode = t.LanguageCode,
                    Name = t.Name,
                    CreatedBy = auditId,
                })
                .ToList(),
        };

        _context.GlobalIngredients.Add(ingredient);
        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<GlobalIngredientDto>.SuccessWithData(GlobalIngredientMapper.ToDto(ingredient));
    }
}
