namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record TableBillGuestAmountDto(int GuestNumber, decimal Amount, string Status);
