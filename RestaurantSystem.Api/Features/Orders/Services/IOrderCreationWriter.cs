using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Interfaces;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Writes an order's financial acceptance inside the caller-owned transaction.</summary>
public interface IOrderCreationWriter
{
    /// <summary>Native acceptance refuses points races instead of silently accepting a higher price.</summary>
    Task<string?> WriteNativeAsync(CreateOrderCommand command, OrderDraft draft, string acceptedCurrency,
        CancellationToken cancellationToken);

    Task<string?> WriteAsync(CreateOrderCommand command, OrderDraft draft, string? acceptedCurrency,
        Func<Task>? beforeSave, CancellationToken cancellationToken);
}
