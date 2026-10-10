using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Products.Services;

internal static class CustomerStepManifestStore
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static CustomerStepManifestDto? Read(Product product)
    {
        if (product.CustomerStepManifestJson is null) return null;
        var stored = JsonSerializer.Deserialize<CustomerStepManifestDto>(product.CustomerStepManifestJson, JsonOptions)
            ?? throw new JsonException("The stored customer-step manifest is empty.");
        if (stored.SchemaVersion != CurrentSchemaVersion)
            throw new BadRequestException("The stored customer-step manifest uses an unsupported schema version.");
        return stored with { Revision = product.CustomerStepManifestRevision };
    }

    public static void Write(Product product, CustomerStepManifestDto manifest, int revision)
    {
        var stored = manifest with { SchemaVersion = CurrentSchemaVersion, Revision = revision };
        product.CustomerStepManifestJson = JsonSerializer.Serialize(stored, JsonOptions);
        product.CustomerStepManifestRevision = revision;
    }

    public static Dictionary<Guid, CompositionRole>? IngredientRolesFor(
        IReadOnlyList<CustomerStepManifestStepDto>? steps,
        Guid productId,
        Guid? sectionItemId = null)
    {
        if (steps is null) return null;
        var roles = steps
            .Where(step => sectionItemId.HasValue
                ? (step.Kind is CustomerStepKind.BundleComponentIngredient or CustomerStepKind.BundleComponentSauce)
                    && step.ProductId == productId
                    && step.SectionItemId == sectionItemId
                    && step.ScopeId.HasValue
                    && step.CompositionRole.HasValue
                : (step.Kind is CustomerStepKind.ProductIngredient or CustomerStepKind.ProductSauce)
                    && step.TargetId.HasValue
                    && step.CompositionRole.HasValue)
            .ToDictionary(
                step => sectionItemId.HasValue
                    ? step.ScopeId.GetValueOrDefault()
                    : step.TargetId.GetValueOrDefault(),
                step => step.CompositionRole.GetValueOrDefault());
        return roles.Count == 0 ? null : roles;
    }

    public static async Task ApplyAsync(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context,
        Product product,
        bool specified,
        CustomerStepManifestDto? manifest,
        CancellationToken cancellationToken)
    {
        if (!specified)
        {
            var stored = Read(product);
            if (stored is not null)
                await CustomerStepManifestRules.ValidateAsync(context, product, stored, cancellationToken);
            return;
        }

        if (manifest is null)
            throw new BadRequestException("customerStepManifest cannot be null; send an empty steps array to reset it.");
        if (manifest.Revision != product.CustomerStepManifestRevision)
            throw new ConflictException(
                $"Customer-step manifest revision conflict; current revision is {product.CustomerStepManifestRevision}.",
                "customer_step_manifest_revision_conflict", product.CustomerStepManifestRevision);

        await CustomerStepManifestRules.ValidateAsync(context, product, manifest, cancellationToken);
        Write(product, manifest, checked(product.CustomerStepManifestRevision + 1));
    }

    public static async Task<ConflictException> RevisionWriteConflictAsync(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context,
        Guid productId, Exception exception, CancellationToken cancellationToken)
    {
        var currentRevision = await context.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.CustomerStepManifestRevision)
            .SingleAsync(cancellationToken);
        return new ConflictException(
            $"Customer-step manifest revision conflict; current revision is {currentRevision}.",
            exception, "customer_step_manifest_revision_conflict", currentRevision);
    }

    public static bool IsRevisionWriteConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || PostgresConcurrencyAborts.IsMatch(exception, out var sqlState)
            && sqlState is PostgresConcurrencyAborts.SerializationFailure or PostgresConcurrencyAborts.Deadlock;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new StringEnumConverterFactory());
        return options;
    }
}
