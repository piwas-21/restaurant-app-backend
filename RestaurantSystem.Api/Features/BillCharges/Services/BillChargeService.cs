using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.BillCharges.Services;

public sealed class BillChargeService(ApplicationDbContext context, ICurrentUserService currentUser,
    IOptions<CurrentStaffTicketSettings> settings) : IBillChargeService
{
    public async Task ApplyAsync(Order order, IReadOnlyList<BillChargeInputDto>? inputs, Guid operationId,
        string? currency, CancellationToken cancellationToken)
    {
        if (inputs is null) throw new BadRequestException("Bill charges must be a list.");
        if (inputs.Count == 0) return;
        if (inputs.Count > BillChargeRules.MaximumRules || inputs.Any(input => input is null) || inputs.Select(i => i.Key).Distinct().Count() != inputs.Count)
            throw new BadRequestException("Choose each bill charge once.");
        var info = await context.RestaurantInfo.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Restaurant settings are unavailable.");
        if (!string.Equals(info.Currency, currency, StringComparison.Ordinal))
            throw new ConflictException("The order currency differs from the current bill charge currency.");
        var rules = BillChargeRules.Read(info.BillChargeRulesJson).ToDictionary(r => r.Key, StringComparer.Ordinal);
        var money = new AccountMoney(currency);
        var role = currentUser.Role?.ToString();
        var existing = OrderBillChargeReader.Read(order).ToList();
        var additions = new List<OrderBillCharge>();
        var limit = settings.Value.MaximumAmendments;
        var amendments = await context.OrderAmendments.AsNoTracking().Where(value => value.SourceOrderId == order.Id
            && value.State == OrderAmendmentState.Committed)
            .OrderBy(value => value.CommittedAt).ThenBy(value => value.CreatedAt).ThenBy(value => value.Id)
            .Select(value => new OrderAmendment
            {
                Id = value.Id,
                CreatedBy = value.CreatedBy,
                SourceOrderId = value.SourceOrderId,
                State = value.State,
                ChangesJson = value.ChangesJson,
                FinancialResolutionJson = value.FinancialResolutionJson
            })
            .Take(limit + 1).ToListAsync(cancellationToken);
        if (amendments.Count > limit)
            throw new ConflictException("This bill exceeds the interactive amendment history limit. Reconcile it before applying adjustments.");
        if (amendments.Any(value => OrderAmendmentFinancialGuard.IsUnresolved(value.FinancialResolutionJson)))
            throw new ConflictException("Resolve the previous correction before adding bill charges.");
        var food = AccountDebtAmendmentProjection.ExcludeVoidedUnits([order], BillDiscountMath.Food(order, money), amendments);
        var basis = AccountDebtMath.Total(food);
        foreach (var input in inputs)
        {
            var rule = ValidateSelection(input, rules, order.Type.ToString(), role, money);
            if (input.Amount == 0) continue;
            if (existing.Any(c => c.Key == input.Key))
                throw new ConflictException("This charge is already on the bill. Refresh the order before collecting payment.");
            var accepted = await CreateAsync(order, input, rule, (food, basis), operationId, money, cancellationToken);
            if (accepted.Allocations is { Count: > 0 })
                food = AccountDebtMath.Subtract(food, accepted.Allocations.Select(value => new AccountDebtSegment(order.Id,
                    value.OrderItemId, value.StartOrdinal, value.Count, value.MinorPerUnit)).ToArray());
            additions.Add(accepted);
        }
        existing.AddRange(additions);
        order.BillChargesJson = JsonSerializer.Serialize(existing, BillChargeRules.JsonOptions);
        order.BillChargeAmount = existing.Sum(c => c.Amount);
        order.RemainingAmount = order.PayableTotal - order.TotalPaid;
    }
    private async Task<OrderBillCharge> CreateAsync(Order order, BillChargeInputDto input, BillChargeRuleDto rule,
        (IReadOnlyList<AccountDebtSegment> Food, long Basis) items, Guid operationId, AccountMoney money, CancellationToken cancellationToken)
    {
        if (order.Tax != 0 && (rule.Effect == "Reduce" || rule.Calculation == "Percentage"))
            throw new ConflictException("This bill has frozen tax. Use tax reconciliation before applying a discount or percentage adjustment.");
        var amount = rule.Calculation == "Percentage"
            ? checked(decimal.ToInt64(decimal.Round(items.Basis * input.Amount / 100m, 0, MidpointRounding.AwayFromZero)))
            : money.ToMinor(input.Amount);
        if (amount == 0)
            throw new BadRequestException("The selected adjustment rounds to zero. Choose a larger amount.");
        IReadOnlyList<BillDiscountAllocation>? allocations = null;
        if (rule.Effect == "Reduce")
        {
            await RequireCanReduceAsync(order, cancellationToken);
            allocations = BillDiscountMath.Allocate(items.Food, amount);
        }
        var signed = money.ToMajor(amount) * (rule.Effect == "Reduce" ? -1m : 1m);
        return new OrderBillCharge(operationId, rule.Key, rule.Label.Trim(), signed,
            money.Currency, DateTime.UtcNow, currentUser.GetAuditIdentifier(), input.Amount,
            rule.Effect ?? "Increase", rule.Calculation ?? "Fixed", items.Basis, allocations);
    }

    private async Task RequireCanReduceAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.ServiceSessionId.HasValue && await context.TableServiceSessions.AsNoTracking()
            .AnyAsync(value => value.Id == order.ServiceSessionId && value.BillingAllocationVersion == 0, cancellationToken))
            throw new ConflictException("Reconcile this legacy table account before applying a discount.");
        // A discretionary reduction never moves existing captured money or creates a refund.
        if (order.TotalPaid != 0 || order.Payments.Any(value => value.Status.IsCaptured())
            || order.ServiceSessionId.HasValue && await context.AccountPaymentAttempts.AsNoTracking()
                .AnyAsync(value => value.ServiceSessionId == order.ServiceSessionId
                    && value.State == AccountPaymentState.Captured
                    && value.Allocations.Any(allocation => allocation.OrderId == order.Id), cancellationToken))
            throw new ConflictException("Apply discounts before taking payment. A paid bill requires the refund workflow.");
    }

    private static BillChargeRuleDto ValidateSelection(BillChargeInputDto input,
        Dictionary<string, BillChargeRuleDto> rules, string orderType, string? role, AccountMoney money)
    {
        if (!rules.TryGetValue(input.Key, out var rule) || !rule.Enabled
            || !rule.OrderTypes.Contains(orderType, StringComparer.Ordinal)
            || role != "Admin" && !rule.Roles.Contains(role ?? string.Empty, StringComparer.Ordinal))
            throw new BadRequestException("This bill charge is not enabled for this order or staff role.");
        if ((input.Effect ?? "Increase") != rule.Effect || (input.Calculation ?? "Fixed") != rule.Calculation)
            throw new ConflictException("The bill adjustment type changed. Refresh the fields and review the amount again.");
        if (input.Amount < 0 || input.Amount > (rule.Calculation == "Percentage" ? 100m : BillChargeRules.MaximumCharge)
            || !rule.AllowCustomAmount && input.Amount != 0 && input.Amount != rule.DefaultAmount)
            throw new BadRequestException("Use the configured bill charge amount.");
        money.ToMinor(input.Amount);
        return rule;
    }

}
