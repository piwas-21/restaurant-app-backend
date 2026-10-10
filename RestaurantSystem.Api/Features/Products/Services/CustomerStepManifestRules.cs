using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Services;

internal static partial class CustomerStepManifestRules
{
    public static async Task ValidateAsync(
        ApplicationDbContext context, Product product, CustomerStepManifestDto manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.SchemaVersion != CustomerStepManifestStore.CurrentSchemaVersion || manifest.Revision < 0)
            throw new BadRequestException("The customer-step manifest version is invalid.");
        if (manifest.Steps is null)
            throw new BadRequestException("Customer-step manifest steps are required.");

        ValidateShape(manifest.Steps);
        ValidateVariationScreens(manifest.Steps);
        ValidateRequiredBeforeExtras(manifest.Steps);
        if (product.Type == ProductType.Menu)
        {
            await ValidateBundleAsync(context, product, manifest.Steps, cancellationToken);
            return;
        }

        ValidateProduct(product, manifest.Steps);
    }

    private static void ValidateShape(IReadOnlyList<CustomerStepManifestStepDto> steps)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
            ValidateStepShape(step, identities);

        if (HasMultipleScreensAtSameOrder(steps))
            throw new BadRequestException("Rows sharing a presentation order must belong to one customer screen.");
    }

    private static void ValidateStepShape(
        CustomerStepManifestStepDto step, HashSet<string> identities)
    {
        if (step is null)
            throw new BadRequestException("Customer-step manifest cannot contain null steps.");
        if (step.PresentationOrder < 0 || step.PresentationLabel?.Length > 120)
            throw new BadRequestException("Customer-step order or label is invalid.");
        if (step.CompositionRole == CompositionRole.Unknown)
            throw new BadRequestException("Unknown composition roles are read-only.");
        if (step.PresentationLabel is not null && step.CompositionRole != CompositionRole.Dish)
            throw new BadRequestException("Presentation labels are supported only for explicit Dish relationships.");
        if (!identities.Add(StepIdentity(step)))
            throw new BadRequestException("The customer-step manifest contains a duplicate stable reference.");
    }

    private static bool HasMultipleScreensAtSameOrder(IReadOnlyList<CustomerStepManifestStepDto> steps) =>
        steps.GroupBy(step => step.PresentationOrder)
            .Any(group => HasMultipleScreens(group));

    private static bool HasMultipleScreens(IEnumerable<CustomerStepManifestStepDto> steps) =>
        steps.Select(ScreenIdentity).Distinct(StringComparer.Ordinal).Skip(1).Any();

    private static void ValidateProduct(Product product, IReadOnlyList<CustomerStepManifestStepDto> steps)
    {
        foreach (var step in steps)
        {
            if (IsBundleKind(step.Kind))
                throw new BadRequestException("Bundle steps cannot target a standalone product.");
            var valid = step.Kind switch
            {
                CustomerStepKind.ProductVariation => step.TargetId.HasValue
                    && product.Variations.Any(row => row.Id == step.TargetId),
                CustomerStepKind.ProductIngredient => step.TargetId.HasValue
                    && product.DetailedIngredients.Any(row => row.Id == step.TargetId && row.Kind == IngredientKind.Ingredient),
                CustomerStepKind.ProductSauce => step.TargetId.HasValue
                    && product.DetailedIngredients.Any(row => row.Id == step.TargetId && row.Kind == IngredientKind.Sauce),
                CustomerStepKind.ProductCustomizationGroup => step.TargetId.HasValue
                    && product.CustomizationGroups.Any(row => row.Id == step.TargetId),
                CustomerStepKind.ProductSuggestedSide => step.TargetId.HasValue
                    && product.SuggestedSideItems.Any(row => row.Id == step.TargetId),
                _ => false
            };
            Require(valid, step);
            ValidateRole(step);
            RequireNoBundleReferences(step);
        }
    }

    private static void ValidateVariationScreens(IReadOnlyList<CustomerStepManifestStepDto> steps)
    {
        var screens = steps.Where(step => step.Kind is CustomerStepKind.ProductVariation
                or CustomerStepKind.BundleComponentVariation or CustomerStepKind.ProductSauce
                or CustomerStepKind.BundleComponentSauce)
            .GroupBy(step => (step.Kind, OwnerId: step.Kind == CustomerStepKind.ProductVariation
                    || step.Kind == CustomerStepKind.ProductSauce
                ? Guid.Empty
                : step.SectionItemId ?? Guid.Empty));

        if (screens.Any(group => group.Select(step => step.PresentationOrder).Distinct().Skip(1).Any()))
            throw new BadRequestException("Variation and sauce options for one selection owner must share one customer screen.");
    }

    private static void ValidateRole(CustomerStepManifestStepDto step)
    {
        if (!step.CompositionRole.HasValue) return;
        var allowed = step.Kind switch
        {
            CustomerStepKind.ProductVariation or CustomerStepKind.BundleComponentVariation =>
                step.CompositionRole is CompositionRole.Dish or CompositionRole.RequiredChoice,
            CustomerStepKind.ProductIngredient or CustomerStepKind.BundleComponentIngredient =>
                step.CompositionRole is CompositionRole.Ingredient or CompositionRole.Extra,
            CustomerStepKind.ProductCustomizationGroup or CustomerStepKind.BundleComponentCustomizationGroup =>
                step.CompositionRole is CompositionRole.RequiredChoice or CompositionRole.Extra,
            CustomerStepKind.ProductSauce or CustomerStepKind.BundleComponentSauce =>
                step.CompositionRole is CompositionRole.Sauce or CompositionRole.Extra,
            CustomerStepKind.ProductSuggestedSide or CustomerStepKind.BundleComponentSide =>
                step.CompositionRole is CompositionRole.Side or CompositionRole.Drink,
            CustomerStepKind.BundleSection => step.CompositionRole is
                CompositionRole.Menu or CompositionRole.Dish or CompositionRole.RequiredChoice
                or CompositionRole.Extra or CompositionRole.Side or CompositionRole.Drink,
            _ => false
        };
        if (!allowed)
            throw new BadRequestException($"Step kind '{step.Kind}' does not support compositionRole '{step.CompositionRole}'.");
    }

    private static void ValidateRequiredBeforeExtras(IReadOnlyList<CustomerStepManifestStepDto> steps)
    {
        foreach (var extra in steps.Where(step => step.CompositionRole == CompositionRole.Extra))
        {
            var requiredChoices = steps.Where(step => step.CompositionRole == CompositionRole.RequiredChoice
                && (IsBundleKind(extra.Kind)
                    ? step.SectionItemId == extra.SectionItemId
                        && step.SectionId == extra.SectionId
                        || step.Kind == CustomerStepKind.BundleSection
                            && step.ParentComponentId == extra.SectionItemId
                    : !IsBundleKind(step.Kind)));
            if (requiredChoices.Any(step => step.PresentationOrder >= extra.PresentationOrder))
                throw new BadRequestException("Required choices must precede optional extras in the customer-step plan.");
        }
    }

    private static string StepIdentity(CustomerStepManifestStepDto step) => string.Join(':',
        step.Kind, step.TargetId, step.SectionId, step.SectionItemId, step.ProductId, step.ScopeId);

    private static string ScreenIdentity(CustomerStepManifestStepDto step) => string.Join(':',
        step.Kind, step.Kind == CustomerStepKind.BundleSection ? step.TargetId : null,
        step.SectionId, step.SectionItemId, step.ProductId, step.CompositionRole);

    private static bool IsBundleKind(CustomerStepKind kind) =>
        kind == CustomerStepKind.BundleSection || IsBundleComponentKind(kind);

    private static bool IsBundleComponentKind(CustomerStepKind kind) => kind is
        CustomerStepKind.BundleComponentVariation or CustomerStepKind.BundleComponentIngredient
        or CustomerStepKind.BundleComponentCustomizationGroup or CustomerStepKind.BundleComponentSauce
        or CustomerStepKind.BundleComponentSide;

    private static void RequireNoBundleReferences(CustomerStepManifestStepDto step)
    {
        if (step.SectionId.HasValue || step.SectionItemId.HasValue || step.ProductId.HasValue
            || step.ScopeId.HasValue || step.ParentComponentId.HasValue)
            throw new BadRequestException("Standalone product steps may only use targetId.");
    }

    private static void Require(bool condition, CustomerStepManifestStepDto step)
    {
        if (!condition) throw new BadRequestException($"Customer-step reference '{step.Kind}' is stale or not owned by this product.");
    }
}
