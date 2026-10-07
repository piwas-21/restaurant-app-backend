using System.Globalization;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderFidelityCoordinator : IOrderFidelityCoordinator
{
    private readonly IOrderNativeFidelityOperations _fidelityPointsService;
    private readonly IOrderPricingService _pricingService;
    private readonly IOrderPaymentBuilder _paymentBuilder;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OrderFidelityCoordinator> _logger;
    private readonly ITenantModules _modules;
    private readonly FidelitySettings _settings;

    public OrderFidelityCoordinator(
        IOrderNativeFidelityOperations fidelityPointsService,
        IOrderPricingService pricingService,
        IOrderPaymentBuilder paymentBuilder,
        ApplicationDbContext context,
        ILogger<OrderFidelityCoordinator> logger,
        ITenantModules modules,
        IOptions<FidelitySettings> settings)
    {
        _fidelityPointsService = fidelityPointsService;
        _pricingService = pricingService;
        _paymentBuilder = paymentBuilder;
        _context = context;
        _logger = logger;
        _modules = modules;
        _settings = settings.Value;
    }

    public async Task<OrderBillingEarningEvaluation?> CalculatePointsToEarnAsync(
        Order order, decimal itemsTotal, Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue || !_modules.IsEnabled(ModuleIds.Loyalty))
        {
            return null;
        }

        var evaluation = await _fidelityPointsService.EvaluateOrderAsync(itemsTotal, cancellationToken);
        order.FidelityPointsEarned = evaluation.CandidatePoints ?? 0;

        _logger.LogInformation("Order will earn {Points} fidelity points", order.FidelityPointsEarned);
        return evaluation;
    }

    public async Task PreviewRedemptionAsync(
        Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue || !pointsToRedeem.HasValue || pointsToRedeem.Value <= 0)
        {
            return;
        }

        EnsureLoyaltyEnabled();
        var balance = await _fidelityPointsService.GetUserBalanceAsync(userId.Value, cancellationToken);
        if (balance is null || balance.CurrentPoints < pointsToRedeem.Value)
        {
            throw InsufficientPoints(balance?.CurrentPoints ?? 0, pointsToRedeem.Value);
        }

        var discountAmount = ValidateDiscountFitsOrder(order, pointsToRedeem.Value);
        order.FidelityPointsRedeemed = pointsToRedeem.Value;
        order.FidelityPointsDiscount = discountAmount;
        _pricingService.RecalculateTotal(order);
        _paymentBuilder.UpdatePaymentSummary(order);
    }

    public async Task<OrderBillingRedemptionEvidence?> RedeemAsync(
        Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken,
        bool failOnError = false)
    {
        if (!userId.HasValue || !pointsToRedeem.HasValue || pointsToRedeem.Value <= 0)
        {
            return null;
        }

        EnsureLoyaltyEnabled();

        try
        {
            var expectedDiscount = ValidateDiscountFitsOrder(order, pointsToRedeem.Value);
            var (transaction, discountAmount) = await _fidelityPointsService.RedeemPointsAsync(
                userId.Value,
                order.Id, // Order must exist in DB by now (caller saves first to avoid FK violation).
                pointsToRedeem.Value,
                cancellationToken);

            if (discountAmount != expectedDiscount)
            {
                throw new BadRequestException("The requested points discount is no longer valid for this order.");
            }

            order.FidelityPointsRedeemed = pointsToRedeem.Value;
            order.FidelityPointsDiscount = discountAmount;

            // The credit only becomes knowable here — redemption FKs the order, so it cannot run
            // until after the insert, and the Total already persisted does not reflect it. Reprice
            // from the order's own columns rather than by subtracting a delta, so this is safe to
            // run more than once.
            _pricingService.RecalculateTotal(order);
            _paymentBuilder.UpdatePaymentSummary(order);

            _logger.LogInformation(
                "Redeemed {Points} fidelity points for ${Discount} discount on order {OrderNumber}",
                pointsToRedeem.Value, discountAmount, order.OrderNumber);

            await _context.SaveChangesAsync(cancellationToken);
            return new OrderBillingRedemptionEvidence(
                transaction.Id, transaction.UserId
                    ?? throw new ConflictException("The new redemption has no authenticated owner."),
                transaction.OrderId, transaction.TransactionType,
                transaction.Points, discountAmount, transaction.OrderTotal, transaction.CreatedAt);
        }
        catch (InsufficientPointsException ex) when (!failOnError)
        {
            order.FidelityPointsRedeemed = 0;
            order.FidelityPointsDiscount = 0;
            _pricingService.RecalculateTotal(order);
            _paymentBuilder.UpdatePaymentSummary(order);

            _logger.LogWarning(ex, "Insufficient fidelity balance for order {OrderNumber}; no debit was written",
                order.OrderNumber);
            return null;
        }
    }

    private void EnsureLoyaltyEnabled()
    {
        if (!_modules.IsEnabled(ModuleIds.Loyalty))
        {
            throw new NotFoundException(
                "This feature is not enabled for this restaurant.", ErrorCodes.ModuleNotEnabled);
        }
    }

    private static BadRequestException InsufficientPoints(int available, int requested) =>
        new($"Insufficient points. Available: {available}, Requested: {requested}");

    private decimal ValidateDiscountFitsOrder(Order order, int pointsToRedeem)
    {
        if (pointsToRedeem > _settings.MaximumPointsPerRedemption)
        {
            throw new BadRequestException(
                $"Cannot redeem more than {_settings.MaximumPointsPerRedemption.ToString("N0", CultureInfo.InvariantCulture)} points at once.");
        }

        var discountAmount = _fidelityPointsService.CalculateDiscountFromPoints(pointsToRedeem);
        var maximumDiscount = Math.Max(0m, order.Total - Math.Max(0m, order.Tip));
        if (discountAmount > maximumDiscount)
        {
            throw new BadRequestException(
                $"Points discount ({discountAmount:C}) cannot exceed the order's discountable amount ({maximumDiscount:C}).");
        }

        return discountAmount;
    }

    public async Task AwardEarnedPointsAsync(
        Order order, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _fidelityPointsService.AwardAcceptedOrderAsync(order.Id, cancellationToken);
            if (result.Disposition == FidelityPointsAwardDisposition.Awarded)
                _logger.LogInformation("Awarded {Points} fidelity points for order {OrderNumber}",
                    result.AppliedPoints, order.OrderNumber);
        }
        catch (Exception ex)
            when (PostgresConcurrencyAborts.IsMatch(ex, out _) && _context.Database.CurrentTransaction is not null)
        {
            // Do not swallow a PostgreSQL abort inside the order-creation transaction. The caller
            // must see the failed transaction rather than report success for rows that rolled back.
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort: order is already created, the points-award failure
            // shouldn't take it down.
            _logger.LogError(
                ex, "Failed to award fidelity points for order {OrderNumber}, but order was created successfully",
                order.OrderNumber);
        }
    }
}
