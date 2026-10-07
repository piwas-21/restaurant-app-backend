namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Current authority for exactly one cached correction job and logical destination.</summary>
public sealed record PrinterUpdateAuthorizationDto(Guid JobId, int Revision, string Target, string Status);
