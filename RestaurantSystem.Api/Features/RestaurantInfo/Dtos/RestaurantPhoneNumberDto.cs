namespace RestaurantSystem.Api.Features.RestaurantInfo.Dtos;

public record RestaurantPhoneNumberDto(
    Guid Id,
    string? Label,
    string Number,
    bool WhatsAppEnabled,
    int DisplayOrder,
    bool IsActive);
