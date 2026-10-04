using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed class OrderAmendmentSupplementBuilder
{
    private readonly IStaffCounterOrderBuilder _builder;
    private readonly ApplicationDbContext _context;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly IOrderPricingService _pricing;
    private readonly IOrderPaymentBuilder _payments;

    public OrderAmendmentSupplementBuilder(
        ApplicationDbContext context,
        IStaffCounterOrderBuilder builder,
        IOrderFidelityCoordinator fidelity,
        IOrderPricingService pricing,
        IOrderPaymentBuilder payments)
    {
        _context = context;
        _builder = builder;
        _fidelity = fidelity;
        _pricing = pricing;
        _payments = payments;
    }

    internal async Task<Order?> BuildAsync(
        Order source,
        OrderAmendmentQuoteRequest request,
        CancellationToken cancellationToken) =>
        (await BuildForAcceptanceAsync(source, request, cancellationToken))?.Order;

    internal Task<OrderAmendmentSupplementBuild?> BuildForAcceptanceAsync(
        Order source,
        OrderAmendmentQuoteRequest request,
        CancellationToken cancellationToken,
        string? quotedCurrency = null) => BuildCoreAsync(source, request, quotedCurrency, cancellationToken);

    private async Task<OrderAmendmentSupplementBuild?> BuildCoreAsync(
        Order source,
        OrderAmendmentQuoteRequest request,
        string? quotedCurrency,
        CancellationToken cancellationToken)
    {
        var hasItems = request.Additions.Count > 0 || request.Changes.Any(
            change => change.Kind == OrderAmendmentChangeKind.Replace);
        if (!hasItems)
            return null;

        var requiredAcceptedCurrency = await OrderNativeAcceptedCurrency.ReadOrderCurrencyEvidenceAsync(
            _context, source, cancellationToken);
        requiredAcceptedCurrency = OrderNativeAcceptedCurrency.ResolveConsistentEvidence(
            [requiredAcceptedCurrency, quotedCurrency]);
        var build = await _builder.BuildAsync(
            OrderAmendmentRequestFactory.CreateSupplement(source, request),
            request.ReleaseAdditionsToKitchen, cancellationToken,
            requiredAcceptedCurrency: requiredAcceptedCurrency);
        if (source.Type == OrderType.Delivery)
        {
            build.Order.DeliveryFee = 0m;
            _pricing.RecalculateTotal(build.Order);
            _payments.UpdatePaymentSummary(build.Order);
        }

        await _fidelity.PreviewRedemptionAsync(
            build.Order, request.PointsToRedeem, build.CustomerUserId, cancellationToken);
        OrderAmendmentJson.EnsureItemIdentity(build.Order);
        return new(build.Order, build.CustomerUserId, build.AcceptedCurrency, build.EarningEvaluation);
    }
}

internal sealed record OrderAmendmentSupplementBuild(
    Order Order,
    Guid? CustomerUserId,
    string? AcceptedCurrency,
    OrderBillingEarningEvaluation? EarningEvaluation);
