using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static partial class CatalogueImportSessionRules
{
    public const int MaximumSelectedTemplates = 128;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateIdPattern();

    [GeneratedRegex("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalePattern();

    public static void ValidateCreateRequest(CreateCatalogueImportSessionRequest request)
    {
        if (!TemplateIdPattern().IsMatch(request.TemplateId) || request.TemplateId.Length > 120 || request.Revision < 1 ||
            !LocalePattern().IsMatch(request.Locale) || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Trim().Length > 128)
        {
            throw new BadRequestException("Invalid catalogue import session request");
        }

        if (request.SelectedTemplateIds is not null)
        {
            ValidateUnique(request.SelectedTemplateIds, "selected template IDs");
        }
    }

    public static void EnsureSameCreateIntent(
        CatalogueImportSession session,
        CreateCatalogueImportSessionRequest request)
    {
        if (session.RootTemplateId != request.TemplateId || session.RootRevision != request.Revision ||
            !string.Equals(session.Locale, request.Locale.Trim(), StringComparison.OrdinalIgnoreCase) ||
            session.CreateNewCopy != request.CreateNewCopy)
        {
            throw new ConflictException("Idempotency key was already used for a different import session");
        }

        var requestedSelection = CatalogueSessionSelection.ComputeSelectedKeys(session, request.SelectedTemplateIds)
            .Select(key => key.TemplateId);
        if (!string.Equals(session.CreateSelectionJson, SerializeSelection(requestedSelection), StringComparison.Ordinal))
        {
            throw new ConflictException("Idempotency key was already used for a different import session");
        }
    }

    public static string SerializeSelection(IEnumerable<string> selectedTemplateIds) =>
        JsonSerializer.Serialize(selectedTemplateIds.OrderBy(value => value, StringComparer.Ordinal), JsonOptions);

    public static void ValidateDecision(CatalogueImportItemDecision decision, string templateType)
    {
        if (decision.Revision < 1 || !IsSupportedResolution(decision.Resolution))
        {
            throw new BadRequestException("Decision resolution must be Create or Reuse");
        }

        if (IsReuse(decision.Resolution) && (decision.LocalEntityId is null || decision.LocalEntityId == Guid.Empty))
        {
            throw new BadRequestException("Reuse decisions require a local entity ID");
        }

        if (HasInvalidLocalValues(decision, templateType))
        {
            throw new BadRequestException("Import decision contains an invalid local value");
        }

        if (HasInvalidRejectedCandidates(decision))
        {
            throw new BadRequestException("Import decision contains invalid rejected candidates");
        }
    }

    private static bool IsSupportedResolution(string resolution) =>
        resolution.Equals("Create", StringComparison.OrdinalIgnoreCase) || IsReuse(resolution);

    private static bool IsReuse(string resolution) =>
        resolution.Equals("Reuse", StringComparison.OrdinalIgnoreCase);

    private static bool HasInvalidLocalValues(CatalogueImportItemDecision decision, string templateType) =>
        HasInvalidPrice(decision, templateType) || decision.AvailableOrderTypes is < 1 or > 7 ||
        HasInvalidReviewLists(decision) || HasInvalidOptionPrices(decision) ||
        decision.LocalName?.Length > 200 || decision.LocalDescription?.Length > 1000 ||
        HasInvalidProductType(decision.LocalProductType) ||
        decision.KitchenType.HasValue && !Enum.IsDefined(decision.KitchenType.Value);

    private static bool HasInvalidPrice(CatalogueImportItemDecision decision, string templateType) =>
        decision.LocalPrice is < 0 || (templateType is "item" or "bundle") && decision.LocalPrice is <= 0;

    private static bool HasInvalidReviewLists(CatalogueImportItemDecision decision) =>
        decision.Ingredients?.Count > 80 || decision.Allergens?.Count > 80 ||
        decision.Ingredients?.Any(string.IsNullOrWhiteSpace) == true ||
        decision.Allergens?.Any(string.IsNullOrWhiteSpace) == true;

    private static bool HasInvalidOptionPrices(CatalogueImportItemDecision decision) =>
        decision.LocalOptionPrices?.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0) == true;

    private static bool HasInvalidProductType(string? productType) =>
        productType is not null && productType is not ("MainItem" or "Beverage" or "Dessert" or "Sauce" or "AddOn");

    private static bool HasInvalidRejectedCandidates(CatalogueImportItemDecision decision) =>
        decision.RejectedCandidateIds?.Count > 6 ||
        decision.RejectedCandidateIds?.Distinct().Count() != decision.RejectedCandidateIds?.Count;

    public static void ValidateUnique(List<string> values, string field)
    {
        if (values.Count > MaximumSelectedTemplates || values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            throw new BadRequestException($"Invalid {field}");
        }
    }
}
