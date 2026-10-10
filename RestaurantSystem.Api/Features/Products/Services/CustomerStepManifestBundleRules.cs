using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Services;

internal static partial class CustomerStepManifestRules
{
    private static async Task ValidateBundleAsync(
        ApplicationDbContext context, Product product,
        IReadOnlyList<CustomerStepManifestStepDto> steps, CancellationToken cancellationToken)
    {
        var sections = product.MenuDefinition?.Sections.ToList() ?? [];
        var sectionRows = sections.SelectMany(section => section.Items.Select(item => (Section: section, Item: item)))
            .ToDictionary(pair => pair.Item.Id);
        var components = await LoadBundleComponentsAsync(context, steps, cancellationToken);

        foreach (var step in steps)
            ValidateBundleStep(step, sections, sectionRows, components, steps);

        ValidateDependencies(steps, sections, sectionRows);
        ValidateBundleComponentOrder(steps, steps.Where(step =>
            step.Kind == CustomerStepKind.BundleSection && step.TargetId.HasValue)
            .ToDictionary(step => step.TargetId!.Value), sectionRows);
    }

    private static async Task<Dictionary<Guid, Product>> LoadBundleComponentsAsync(
        ApplicationDbContext context,
        IReadOnlyList<CustomerStepManifestStepDto> steps,
        CancellationToken cancellationToken)
    {
        var componentIds = steps.Where(step => IsBundleComponentKind(step.Kind) && step.ProductId.HasValue)
            .Select(step => step.ProductId.GetValueOrDefault()).Distinct().ToList();
        return await context.Products.AsNoTracking().AsSplitQuery()
            .Where(candidate => componentIds.Contains(candidate.Id) && !candidate.IsDeleted)
            .Include(candidate => candidate.Variations)
            .Include(candidate => candidate.DetailedIngredients)
            .Include(candidate => candidate.CustomizationGroups)
            .Include(candidate => candidate.SuggestedSideItems)
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
    }

    private static void ValidateBundleStep(
        CustomerStepManifestStepDto step,
        IReadOnlyList<MenuSection> sections,
        IReadOnlyDictionary<Guid, (MenuSection Section, MenuSectionItem Item)> sectionRows,
        IReadOnlyDictionary<Guid, Product> components,
        IReadOnlyList<CustomerStepManifestStepDto> steps)
    {
        if (step.Kind == CustomerStepKind.BundleSection)
        {
            ValidateBundleSectionStep(step, sections);
            return;
        }

        if (!IsBundleComponentKind(step.Kind))
            throw new BadRequestException("A bundle manifest contains an unsupported step kind.");
        ValidateBundleComponentStep(step, sectionRows, components, steps);
    }

    private static void ValidateBundleSectionStep(
        CustomerStepManifestStepDto step, IReadOnlyList<MenuSection> sections)
    {
        var sectionExists = step.TargetId.HasValue && sections.Any(section => section.Id == step.TargetId);
        Require(sectionExists, step);
        if (step.SectionId.HasValue || step.SectionItemId.HasValue || step.ProductId.HasValue || step.ScopeId.HasValue)
            throw new BadRequestException("Bundle section steps may only use targetId and parentComponentId.");
        ValidateRole(step);
    }

    private static void ValidateBundleComponentStep(
        CustomerStepManifestStepDto step,
        IReadOnlyDictionary<Guid, (MenuSection Section, MenuSectionItem Item)> sectionRows,
        IReadOnlyDictionary<Guid, Product> components,
        IReadOnlyList<CustomerStepManifestStepDto> steps)
    {
        var owner = ResolveBundleOwner(step, sectionRows);
        ValidateBundleLabelOwner(step, owner.Section, steps);
        var productId = step.ProductId.GetValueOrDefault();
        if (!components.TryGetValue(productId, out var component))
            throw new BadRequestException("A bundle component product is missing or deleted.");
        Require(ScopeBelongsToComponent(step, component), step);
        ValidateRole(step);
    }

    private static (MenuSection Section, MenuSectionItem Item) ResolveBundleOwner(
        CustomerStepManifestStepDto step,
        IReadOnlyDictionary<Guid, (MenuSection Section, MenuSectionItem Item)> sectionRows)
    {
        if (!IsBundleComponentKind(step.Kind) || step.TargetId.HasValue || !step.SectionId.HasValue
            || !step.SectionItemId.HasValue || !step.ProductId.HasValue || !step.ScopeId.HasValue
            || step.ParentComponentId.HasValue)
            throw new BadRequestException("Bundle component steps require sectionId, sectionItemId, productId, and scopeId.");

        if (!sectionRows.TryGetValue(step.SectionItemId.Value, out var owner)
            || owner.Section.Id != step.SectionId || owner.Item.ProductId != step.ProductId)
            throw new BadRequestException("The bundle component selection is stale or is not owned by its section.");
        return owner;
    }

    private static void ValidateBundleLabelOwner(
        CustomerStepManifestStepDto step, MenuSection section,
        IReadOnlyList<CustomerStepManifestStepDto> steps)
    {
        if (step.Kind != CustomerStepKind.BundleComponentVariation || step.PresentationLabel is null)
            return;
        var ownerStep = steps.FirstOrDefault(candidate =>
            candidate.Kind == CustomerStepKind.BundleSection && candidate.TargetId == section.Id);
        if (ownerStep?.CompositionRole != CompositionRole.Dish)
            throw new BadRequestException(
                "A component variation label requires an explicitly Dish-role bundle section.");
    }

    private static bool ScopeBelongsToComponent(CustomerStepManifestStepDto step, Product component) =>
        step.Kind switch
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


}
