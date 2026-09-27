using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Catalogue.Dtos;

public sealed record UpdateCatalogueImportSessionRequest
{
    [JsonRequired]
    public int ExpectedVersion { get; init; }
    public List<string> SelectedTemplateIds { get; init; } = [];
    public List<CatalogueImportItemDecision> Decisions { get; init; } = [];
}

public sealed record CatalogueImportItemDecision
{
    public string TemplateId { get; init; } = string.Empty;
    public int Revision { get; init; }
    public string Resolution { get; init; } = string.Empty;
    public Guid? LocalEntityId { get; init; }
    public string? LocalName { get; init; }
    public string? LocalDescription { get; init; }
    public decimal? LocalPrice { get; init; }
    public string? LocalProductType { get; init; }
    public bool? IntendedIsAvailable { get; init; }
    public List<string>? Ingredients { get; init; }
    public List<string>? Allergens { get; init; }
    public bool? IngredientsReviewed { get; init; }
    public bool? AllergensReviewed { get; init; }
    public bool? AvailabilityReviewed { get; init; }
    public bool? ChannelsReviewed { get; init; }
    public bool? KitchenRoutingReviewed { get; init; }
    public bool? OptionPricesReviewed { get; init; }
    public bool? ChoiceRulesReviewed { get; init; }
    public Dictionary<string, decimal>? LocalOptionPrices { get; init; }
    public List<Guid>? RejectedCandidateIds { get; init; }
    public int? AvailableOrderTypes { get; init; }
    public KitchenType? KitchenType { get; init; }
}
