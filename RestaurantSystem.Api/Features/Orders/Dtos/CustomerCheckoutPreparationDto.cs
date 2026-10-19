namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>A read-only preflight; the digest is a review precondition, never an authorization token.</summary>
public sealed record CustomerCheckoutPreparationDto(int SchemaVersion, string PayloadHash, CustomerCheckoutQuoteDto Quote);
