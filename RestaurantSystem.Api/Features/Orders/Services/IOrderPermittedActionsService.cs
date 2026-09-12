using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Builds the authenticated caller's explainable order action set.</summary>
public interface IOrderPermittedActionsService
{
    IReadOnlyList<OrderPermittedActionDto> GetPermittedActions(Order order);
}
