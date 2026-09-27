using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed class TranslationTextReader(ApplicationDbContext context) : ITranslationTextReader
{
    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        TranslationFieldInputDto input,
        CancellationToken cancellationToken)
    {
        if (input.TargetTexts is not null)
        {
            return input.TargetTexts;
        }

        var reference = input.FieldRef;
        if (reference.EntityId is not Guid id)
        {
            return new Dictionary<string, string>();
        }

        return reference.EntityType switch
        {
            "product" => await ProductAsync(id, reference.FieldKey, cancellationToken),
            "productVariation" => await VariationAsync(id, reference.FieldKey, cancellationToken),
            "productIngredient" => await IngredientAsync(id, cancellationToken),
            "menuSection" => await SectionAsync(id, reference.FieldKey, cancellationToken),
            "optionSet" => await OptionSetAsync(id, cancellationToken),
            _ => throw new BadRequestException("Unsupported translation entity")
        };
    }

    private async Task<IReadOnlyDictionary<string, string>> ProductAsync(
        Guid id, string field, CancellationToken cancellationToken)
    {
        var product = await context.Products.AsNoTracking()
            .Include(row => row.Descriptions)
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product was not found");
        return product.Descriptions.ToDictionary(
            row => row.Lang,
            row => field == "name" ? row.Name : row.Description,
            StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, string>> VariationAsync(
        Guid id, string field, CancellationToken cancellationToken)
    {
        var variation = await context.ProductVariations.AsNoTracking()
            .Include(row => row.Descriptions)
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product variation was not found");
        return variation.Descriptions.ToDictionary(
            row => row.LanguageCode,
            row => field == "name" ? row.Name : row.Description ?? string.Empty,
            StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, string>> IngredientAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var ingredient = await context.ProductIngredients.AsNoTracking()
            .Include(row => row.Descriptions)
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product ingredient was not found");
        return ingredient.Descriptions.ToDictionary(
            row => row.LanguageCode, row => row.Name, StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, string>> SectionAsync(
        Guid id, string field, CancellationToken cancellationToken)
    {
        var section = await context.MenuSections.AsNoTracking()
            .Include(row => row.Translations)
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException("Menu section was not found");
        return section.Translations.ToDictionary(
            row => row.LanguageCode,
            row => field == "name" ? row.Name : row.Description ?? string.Empty,
            StringComparer.Ordinal);
    }

    private async Task<IReadOnlyDictionary<string, string>> OptionSetAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var set = await context.OptionSets.AsNoTracking().Include(row => row.Translations)
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException("Option set was not found");
        var texts = set.Translations.ToDictionary(row => row.LanguageCode, row => row.Name,
            StringComparer.Ordinal);
        texts.TryAdd(set.SourceLocale, set.Name);
        return texts;
    }
}
