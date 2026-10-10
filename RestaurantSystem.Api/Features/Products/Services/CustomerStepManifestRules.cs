using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Services;

internal static class CustomerStepManifestRules
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
        {
            if (step is null)
                throw new BadRequestException("Customer-step manifest cannot contain null steps.");
            if (step.PresentationOrder < 0 || step.PresentationLabel?.Length > 120)
                throw new BadRequestException("Customer-step order or label is invalid.");
            if (step.CompositionRole == CompositionRole.Unknown)
                throw new BadRequestException("Unknown composition roles are read-only.");
            if (step.PresentationLabel is not null && step.CompositionRole != CompositionRole.Dish)
                throw new BadRequestException("Presentation labels are supported only for explicit Dish relationships.");

            var identity = StepIdentity(step);
            if (!identities.Add(identity))
                throw new BadRequestException("The customer-step manifest contains a duplicate stable reference.");
        }

        foreach (var orderGroup in steps.GroupBy(step => step.PresentationOrder))
        {
            if (orderGroup.Select(ScreenIdentity).Distinct(StringComparer.Ordinal).Skip(1).Any())
                throw new BadRequestException("Rows sharing a presentation order must belong to one customer screen.");
        }
    }

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

    private static async Task ValidateBundleAsync(
        ApplicationDbContext context, Product product,
        IReadOnlyList<CustomerStepManifestStepDto> steps, CancellationToken cancellationToken)
    {
        var sections = product.MenuDefinition?.Sections.ToList() ?? [];
        var sectionRows = sections.SelectMany(section => section.Items.Select(item => (Section: section, Item: item)))
            .ToDictionary(pair => pair.Item.Id);
        var componentIds = steps.Where(step => IsBundleComponentKind(step.Kind) && step.ProductId.HasValue)
            .Select(step => step.ProductId!.Value).Distinct().ToList();
        var components = await context.Products.AsNoTracking().AsSplitQuery()
            .Where(candidate => componentIds.Contains(candidate.Id) && !candidate.IsDeleted)
            .Include(candidate => candidate.Variations)
            .Include(candidate => candidate.DetailedIngredients)
            .Include(candidate => candidate.CustomizationGroups)
            .Include(candidate => candidate.SuggestedSideItems)
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);

        foreach (var step in steps)
        {
            if (step.Kind == CustomerStepKind.BundleSection)
            {
                var sectionExists = step.TargetId.HasValue && sections.Any(section => section.Id == step.TargetId);
                Require(sectionExists, step);
                if (step.SectionId.HasValue || step.SectionItemId.HasValue || step.ProductId.HasValue || step.ScopeId.HasValue)
                    throw new BadRequestException("Bundle section steps may only use targetId and parentComponentId.");
                ValidateRole(step);
                continue;
            }

            if (!IsBundleComponentKind(step.Kind) || step.TargetId.HasValue || !step.SectionId.HasValue
                || !step.SectionItemId.HasValue || !step.ProductId.HasValue || !step.ScopeId.HasValue
                || step.ParentComponentId.HasValue)
                throw new BadRequestException("Bundle component steps require sectionId, sectionItemId, productId, and scopeId.");

            var rowMatches = sectionRows.TryGetValue(step.SectionItemId.Value, out var owner)
                && owner.Section.Id == step.SectionId && owner.Item.ProductId == step.ProductId;
            Require(rowMatches, step);
            if (step.Kind == CustomerStepKind.BundleComponentVariation && step.PresentationLabel is not null)
            {
                var ownerStep = steps.FirstOrDefault(candidate =>
                    candidate.Kind == CustomerStepKind.BundleSection && candidate.TargetId == owner.Section.Id);
                if (ownerStep?.CompositionRole != CompositionRole.Dish)
                    throw new BadRequestException(
                        "A component variation label requires an explicitly Dish-role bundle section.");
            }
            if (!components.TryGetValue(step.ProductId.Value, out var component))
                throw new BadRequestException("A bundle component product is missing or deleted.");

            var scopeMatches = step.Kind switch
            {
                CustomerStepKind.BundleComponentVariation => component.Variations.Any(row => row.Id == step.ScopeId),
                CustomerStepKind.BundleComponentIngredient => component.DetailedIngredients.Any(
                    row => row.Id == step.ScopeId && row.Kind == IngredientKind.Ingredient),
                CustomerStepKind.BundleComponentSauce => component.DetailedIngredients.Any(
                    row => row.Id == step.ScopeId && row.Kind == IngredientKind.Sauce),
                CustomerStepKind.BundleComponentCustomizationGroup => component.CustomizationGroups.Any(row => row.Id == step.ScopeId),
                CustomerStepKind.BundleComponentSide => component.SuggestedSideItems.Any(row => row.Id == step.ScopeId),
                _ => false
            };
            Require(scopeMatches, step);
            ValidateRole(step);
        }

        ValidateDependencies(steps, sections, sectionRows);
        ValidateBundleComponentOrder(steps, sectionSteps: steps.Where(step =>
            step.Kind == CustomerStepKind.BundleSection && step.TargetId.HasValue)
            .ToDictionary(step => step.TargetId!.Value), sectionRows);
    }

    private static void ValidateDependencies(
        IReadOnlyList<CustomerStepManifestStepDto> steps,
        IReadOnlyList<MenuSection> sections,
        Dictionary<Guid, (MenuSection Section, MenuSectionItem Item)> sectionRows)
    {
        var sectionSteps = steps.Where(step => step.Kind == CustomerStepKind.BundleSection && step.TargetId.HasValue)
            .ToDictionary(step => step.TargetId!.Value);
        var edges = new Dictionary<Guid, Guid>();
        foreach (var step in sectionSteps.Values)
        {
            if (!step.ParentComponentId.HasValue) continue;
            if (!sectionRows.TryGetValue(step.ParentComponentId.Value, out var parent))
                throw new BadRequestException("A dependent section references a stale parent component.");
            if (!sectionSteps.TryGetValue(parent.Section.Id, out var parentStep)
                || parentStep.PresentationOrder >= step.PresentationOrder
                || parentStep.CompositionRole != CompositionRole.Dish)
                throw new BadRequestException("A parent component selection must precede its dependent section.");
            edges[step.TargetId!.Value] = parent.Section.Id;
        }

        var visited = new HashSet<Guid>();
        var active = new HashSet<Guid>();
        foreach (var section in sections) Visit(section.Id, edges, visited, active);
    }

    private static void Visit(Guid sectionId, IReadOnlyDictionary<Guid, Guid> edges,
        ISet<Guid> visited, ISet<Guid> active)
    {
        if (active.Contains(sectionId))
            throw new BadRequestException("Customer-step section dependencies cannot contain cycles.");
        if (!visited.Add(sectionId)) return;
        active.Add(sectionId);
        if (edges.TryGetValue(sectionId, out var parentId)) Visit(parentId, edges, visited, active);
        active.Remove(sectionId);
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

    private static void ValidateBundleComponentOrder(
        IReadOnlyList<CustomerStepManifestStepDto> steps,
        Dictionary<Guid, CustomerStepManifestStepDto> sectionSteps,
        Dictionary<Guid, (MenuSection Section, MenuSectionItem Item)> sectionRows)
    {
        foreach (var step in steps.Where(candidate => IsBundleComponentKind(candidate.Kind)))
        {
            if (!step.SectionId.HasValue || !step.SectionItemId.HasValue)
                throw new BadRequestException("A component preparation screen must follow its owning section selection.");
            if (!sectionSteps.TryGetValue(step.SectionId.Value, out var ownerStep)
                || ownerStep.PresentationOrder >= step.PresentationOrder)
                throw new BadRequestException("A component preparation screen must follow its owning section selection.");
            if (!sectionRows.TryGetValue(step.SectionItemId.Value, out var owner)
                || owner.Section.Id != step.SectionId.Value)
                throw new BadRequestException("A component preparation screen must follow its owning section selection.");
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
