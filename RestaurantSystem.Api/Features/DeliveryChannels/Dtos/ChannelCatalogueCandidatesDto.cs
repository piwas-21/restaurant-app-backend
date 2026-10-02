namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

public sealed record ChannelCatalogueCandidatesDto(string Currency, string Language,
    string? NextCursor, IReadOnlyList<ChannelCatalogueCandidateDto> Items);

public sealed record ChannelCatalogueCandidateDto(Guid ProductId, Guid? VariationId,
    string Name, string? VariationName, int? PriceMinor, bool Available, bool Supported, string BlockReason);
