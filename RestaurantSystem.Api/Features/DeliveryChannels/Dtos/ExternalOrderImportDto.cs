namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

/// <summary>A durable local identity; this response does not confirm provider acceptance or kitchen release.</summary>
public sealed record ExternalOrderImportDto(Guid OrderId, string OrderNumber, bool AlreadyImported);
