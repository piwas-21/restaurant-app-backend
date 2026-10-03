using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

public sealed record DeliveryChannelCatalogueDraftRequest(
    string? ExpectedDraftRevision,
    IReadOnlyList<DeliveryChannelDraftMappingDto> Items)
{
    public string ExpectedSourceRevision { get; init; } = string.Empty;
    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];
    public IReadOnlyList<DeliveryChannelItemOverrideDto> ItemOverrides { get; init; } = [];
}

public sealed record DeliveryChannelDraftMappingDto(
    string ProviderItemId,
    Guid ProductId,
    Guid? VariationId);

public sealed record DeliveryChannelItemOverrideDto(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] Guid? VariationId,
    [property: JsonRequired] Guid? CategoryId,
    [property: JsonRequired] bool Selected);

public sealed record DeliveryChannelPreviewRequest(string DraftRevision);

public sealed record DeliveryChannelPublishRequest(string DraftRevision, string PublicationRevision)
{
    public bool? ConfirmedTaxProfile { get; init; }
    public string TaxProfileRevision { get; init; } = string.Empty;
}
