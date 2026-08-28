using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Api.Features.GlobalVariations.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalVariations.Commands.UpdateGlobalVariationCommand;

/// <param name="IsActive">
/// <c>null</c> means "say nothing about availability", not <c>false</c>. The ingredient twin takes a
/// non-nullable <c>bool</c> here, so a PUT that merely omits the field deactivates the row and no
/// screen lists an inactive one — backend #428. The new table does not repeat that.
/// </param>
public record UpdateGlobalVariationCommand(
    Guid Id,
    string DefaultName,
    bool? IsActive,
    List<GlobalVariationTranslationDto> Translations
) : ICommand<ApiResponse<GlobalVariationDto>>;

/// <summary>
/// Edits a library row. Editing it does NOT propagate to the products that copied it (plan D3:
/// provenance first, propagation later) — the count this returns is what a later slice will use to
/// tell the admin how many products a propagating edit would touch.
/// </summary>
public class UpdateGlobalVariationCommandHandler : ICommandHandler<UpdateGlobalVariationCommand, ApiResponse<GlobalVariationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateGlobalVariationCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<GlobalVariationDto>> Handle(UpdateGlobalVariationCommand command, CancellationToken cancellationToken)
    {
        var variation = await _context.GlobalVariations
            .Include(g => g.Translations)
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken);

        if (variation == null)
        {
            return ApiResponse<GlobalVariationDto>.Failure("Global variation not found");
        }

        var auditId = _currentUserService.GetAuditIdentifier();

        variation.DefaultName = command.DefaultName.Trim();
        variation.IsActive = command.IsActive ?? variation.IsActive;
        SyncTranslations(variation, command.Translations, auditId);

        await _context.SaveChangesAsync(cancellationToken);

        var usedOnProductCount = await GlobalVariationUsage.CountForAsync(_context, variation.Id, cancellationToken);

        return ApiResponse<GlobalVariationDto>.SuccessWithData(
            GlobalVariationMapper.ToDto(variation, usedOnProductCount));
    }

    private void SyncTranslations(GlobalVariation variation, List<GlobalVariationTranslationDto> incoming, string auditId)
    {
        var incomingCodes = incoming.Select(t => t.LanguageCode).ToHashSet();

        foreach (var translation in variation.Translations.Where(t => !incomingCodes.Contains(t.LanguageCode)).ToList())
        {
            _context.GlobalVariationTranslations.Remove(translation);
        }

        foreach (var dto in incoming)
        {
            var existing = variation.Translations.FirstOrDefault(t => t.LanguageCode == dto.LanguageCode);
            if (existing != null)
            {
                existing.Name = dto.Name;
            }
            else
            {
                variation.Translations.Add(new GlobalVariationTranslation
                {
                    LanguageCode = dto.LanguageCode,
                    Name = dto.Name,
                    CreatedBy = auditId,
                });
            }
        }
    }
}
