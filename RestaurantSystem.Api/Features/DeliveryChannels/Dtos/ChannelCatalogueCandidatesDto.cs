namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

public sealed record ChannelCatalogueCandidatesDto(string Currency, string Language,
    string? NextCursor, IReadOnlyList<ChannelCatalogueCandidateDto> Items, string SourceRevision = "")
{
    public string CategoryBasis { get; init; } = "primaryCategory";
}

public sealed record ChannelCatalogueCandidateDto(Guid ProductId, Guid? VariationId,
    string Name, string? VariationName, int? PriceMinor, bool Available, bool Supported, string BlockReason,
    string SelectionKey = "", Guid? CategoryId = null, string? CategoryName = null,
    int? CategoryDisplayOrder = null, int ItemDisplayOrder = 0);
