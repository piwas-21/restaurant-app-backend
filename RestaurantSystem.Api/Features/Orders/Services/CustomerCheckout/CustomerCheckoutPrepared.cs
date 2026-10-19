using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Transient server-priced inputs and their preview; never a request model or durable receipt.</summary>
public sealed record CustomerCheckoutPrepared(Order Order, IReadOnlyList<CreateOrderItemDto> Items);
