using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.ApplyGlobalIngredientTranslationsCommand;

public record ApplyGlobalIngredientTranslationsCommand(
    Guid Id,
    List<GlobalIngredientTranslationDto> Translations)
    : ICommand<ApiResponse<ApplyGlobalIngredientTranslationsResultDto>>;

/// <summary>
/// Writes one set of per-locale names onto EVERY copy of a library row — the propagation slice the
/// bulk attach (<see cref="AttachGlobalIngredientCommand.AttachGlobalIngredientCommandHandler"/>)
/// deliberately did not do. A partner edits "Mozzarella" on one product, and the same word stays
/// wrong on the other thirty: each attach is a COPY (plan D3), so before this endpoint nothing ever
/// carried an edit back to the copies.
/// </summary>
/// <remarks>
/// <para>
/// <b>The write is a PATCH of names, never a replace.</b> A language in the payload updates or
/// creates its name on the library row and on every copy; a language NOT in the payload is left
/// exactly as it is. A client fixing one locale must not be able to erase the other nine, so
/// "omitted" means "unchanged" — the rule <c>UpdateGlobalIngredientCommand</c> holds for the row
/// itself, extended to the copies.
/// </para>
/// <para>
/// <b>A copy's own <see cref="ProductIngredientDescription.Description"/> is preserved.</b> The
/// payload owns the WORD (the name per locale); the description is the product's own dietary /
/// allergen content and no translation fix has anything to say about it. Only the Name column
/// moves.
/// </para>
/// <para>
/// <b>Which copies are reached.</b> Every copy on a live product that links by id — provenance
/// written at attach time — plus the LEGACY rows that predate provenance and can only be
/// recognised by carrying the row's default name. The match is case-insensitive and by name ONLY
/// when <c>GlobalIngredientId</c> is null, so a linked copy named differently is never mistaken
/// for legacy. An ARCHIVED row is still a valid subject (plan D4: archived is off the shelf, still
/// linked), and a soft-deleted product is reached by nothing: the query runs through
/// <c>Products</c>, so the global filter decides.
/// </para>
/// </remarks>
public class ApplyGlobalIngredientTranslationsCommandHandler
    : ICommandHandler<ApplyGlobalIngredientTranslationsCommand, ApiResponse<ApplyGlobalIngredientTranslationsResultDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public ApplyGlobalIngredientTranslationsCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<ApplyGlobalIngredientTranslationsResultDto>> Handle(
        ApplyGlobalIngredientTranslationsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var library = await _context.GlobalIngredients
            .Include(g => g.Translations)
            .FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Global ingredient not found");

        var auditId = _currentUserService.GetAuditIdentifier();

        UpsertLibraryTranslations(library, command.Translations, auditId);

        var products = await _context.Products
            .Where(p => p.DetailedIngredients.Any(i =>
                i.GlobalIngredientId == command.Id
                || (i.GlobalIngredientId == null && i.Name.ToLower() == library.DefaultName.ToLower())))
            .Include(p => p.DetailedIngredients)
            .ThenInclude(i => i.Descriptions)
            .ToListAsync(cancellationToken);

        var result = new ApplyGlobalIngredientTranslationsResultDto();
        foreach (var product in products)
        {
            // Collected once: the match predicate decides BOTH the per-copy writes and whether the
            // product counts at all, and a product may carry two copies that both match — a linked
            // one and a same-named legacy one — which is two items but one product.
            var copies = product.DetailedIngredients.Where(i => ReferencesLibrary(i, library)).ToList();

            foreach (var copy in copies)
            {
                UpsertCopyTranslations(copy, command.Translations, auditId);
                result.UpdatedIngredientCount++;
                result.Items.Add(new AppliedGlobalIngredientTranslationItemDto(
                    product.Id, product.Name, copy.Id));
            }

            if (copies.Count > 0)
            {
                result.UpdatedProductCount++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<ApplyGlobalIngredientTranslationsResultDto>.SuccessWithData(result);
    }

    private static bool ReferencesLibrary(ProductIngredient ingredient, GlobalIngredient library) =>
        ingredient.GlobalIngredientId == library.Id
        || (ingredient.GlobalIngredientId == null
            && string.Equals(ingredient.Name, library.DefaultName, StringComparison.OrdinalIgnoreCase));

    private void UpsertLibraryTranslations(
        GlobalIngredient library,
        List<GlobalIngredientTranslationDto> incoming,
        string auditId)
    {
        foreach (var dto in incoming)
        {
            var existing = library.Translations.FirstOrDefault(t => t.LanguageCode == dto.LanguageCode);
            if (existing is not null)
            {
                existing.Name = dto.Name;
            }
            else
            {
                library.Translations.Add(new GlobalIngredientTranslation
                {
                    LanguageCode = dto.LanguageCode,
                    Name = dto.Name,
                    CreatedBy = auditId,
                });
            }
        }
    }

    private void UpsertCopyTranslations(
        ProductIngredient copy,
        List<GlobalIngredientTranslationDto> incoming,
        string auditId)
    {
        foreach (var dto in incoming)
        {
            var existing = copy.Descriptions.FirstOrDefault(d => d.LanguageCode == dto.LanguageCode);
            if (existing is not null)
            {
                // The name moves; the description is this product's own content and stays.
                existing.Name = dto.Name;
            }
            else
            {
                copy.Descriptions.Add(new ProductIngredientDescription
                {
                    ProductIngredientId = copy.Id,
                    LanguageCode = dto.LanguageCode,
                    Name = dto.Name,
                    Description = null,
                    CreatedBy = auditId,
                });
            }
        }
    }
}
