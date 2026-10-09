using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Services;

internal static class OrderCheckoutCurrencyEvidence
{
    internal static async Task<string?> ResolveAsync(
        ApplicationDbContext context,
        Order order,
        IReadOnlyCollection<OrderCheckoutSession> priorSessions,
        CancellationToken cancellationToken)
    {
        var acceptedCurrency = await OrderNativeAcceptedCurrency.ReadOrderCurrencyEvidenceAsync(
            context, order, cancellationToken);
        var priorCurrencies = priorSessions.Select(session => session.Currency);
        try
        {
            return OrderNativeAcceptedCurrency.ResolveConsistentEvidence(
                [acceptedCurrency, .. priorCurrencies]);
        }
        catch (ConflictException exception)
        {
            throw new ConflictException(
                "Checkout currency evidence disagrees across the order and its earlier payment attempts.",
                exception);
        }
    }
}
