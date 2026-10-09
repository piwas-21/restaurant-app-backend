using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record ZReportTableAccountTipDto(string Currency, PaymentMethod PaymentMethod, long TipMinor);
