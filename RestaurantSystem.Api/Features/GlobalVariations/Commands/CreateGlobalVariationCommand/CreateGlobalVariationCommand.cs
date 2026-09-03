using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalVariations.Commands.CreateGlobalVariationCommand;

public record CreateGlobalVariationCommand(
    string DefaultName,
    List<GlobalVariationTranslationDto> Translations
) : ICommand<ApiResponse<GlobalVariationDto>>;

/// <summary>
/// Adds a row to the variation library — the "+ Create new" the picker offers when a search finds
/// nothing.
///
/// <para>
/// An empty translation list is legal and deliberate, exactly as it is for ingredients: the row is
/// findable by its default name, and demanding nine translations up front would make the create
/// path harder than typing the variation by hand, which is the thing this library exists to replace.
/// </para>
/// </summary>
public class CreateGlobalVariationCommandHandler : ICommandHandler<CreateGlobalVariationCommand, ApiResponse<GlobalVariationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreateGlobalVariationCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<GlobalVariationDto>> Handle(CreateGlobalVariationCommand command, CancellationToken cancellationToken)
    {
        var auditId = _currentUserService.GetAuditIdentifier();

        var variation = new GlobalVariation
        {
            DefaultName = command.DefaultName.Trim(),
            IsActive = true,
            // The one place a Custom row is born. The column defaults to System so the seeded rows
            // need no backfill, which makes stamping it HERE the whole of what separates the two
            // shelves — see LibraryOrigin.
            Origin = LibraryOrigin.Custom,
            CreatedBy = auditId,
            Translations = command.Translations
                .Select(t => new GlobalVariationTranslation
                {
                    LanguageCode = t.LanguageCode,
                    Name = t.Name,
                    CreatedBy = auditId,
                })
                .ToList(),
        };

        _context.GlobalVariations.Add(variation);
        await _context.SaveChangesAsync(cancellationToken);

        // A row that was created a moment ago is used by nothing, so the count is 0 without asking.
        return ApiResponse<GlobalVariationDto>.SuccessWithData(GlobalVariationMapper.ToDto(variation));
    }
}
