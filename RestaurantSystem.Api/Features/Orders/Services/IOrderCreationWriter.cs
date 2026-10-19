using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Interfaces;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Writes an order's financial acceptance inside the caller-owned transaction.</summary>
public interface IOrderCreationWriter
{
    Task<string?> WriteAsync(CreateOrderCommand command, OrderDraft draft, string? acceptedCurrency,
        Func<Task>? beforeSave, CancellationToken cancellationToken);
}
