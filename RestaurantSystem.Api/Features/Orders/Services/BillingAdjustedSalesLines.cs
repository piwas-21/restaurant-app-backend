using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Retains the report's gross item-price meaning, excluding resolved removed units.</summary>
internal static class BillingAdjustedSalesLines
{
    internal static IReadOnlyList<ReportedSalesLine> Project(
        IReadOnlyList<Order> orders, IReadOnlyList<OrderAmendment> amendments)
    {
        var result = new List<ReportedSalesLine>();
        var byOrder = amendments.GroupBy(value => value.SourceOrderId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<OrderAmendment>)group.ToArray());
        foreach (var order in orders)
        {
            var roots = order.Items.Where(item => item.ParentOrderItemId is null).ToArray();
            var changes = byOrder.GetValueOrDefault(order.Id) ?? [];
            if (order.BillingCreditAmount == 0 && !HasRemoval(changes))
            {
                result.AddRange(roots.Select(item => new ReportedSalesLine(item, item.Quantity, item.ItemTotal)));
                continue;
            }
            foreach (var amendment in changes)
                OrderAmendmentFinancialGuard.AssertResolved(amendment.FinancialResolutionJson);
            var outcome = changes.Select(amendment => OrderAmendmentJson
                .Deserialize<OrderAmendmentFinancialPreviewDto>(amendment.FinancialResolutionJson))
                .FirstOrDefault(value => value.Currency is not null)
                ?? throw new ConflictException("The report's removed units have no currency evidence.");
            var money = new AccountMoney(outcome.Currency);
            var frozen = FrozenOrderChargeMath.Read(order, money);
            var food = Keep(order, frozen.FoodLines.SelectMany(line => AccountDebtMath.CreateLine(
                order.Id, line.Item.Id, line.Item.Quantity, line.AmountMinor, 0)).ToArray(), changes);
            if (frozen.FoodMinor - AccountDebtMath.Total(food) != money.ToMinor(order.BillingCreditAmount))
                throw new ConflictException("The report's removed units and billing credit require reconciliation.");

            // Zero-price dishes still count as units; money segments intentionally omit zero amounts.
            var quantities = Keep(order, roots.Select(item => new AccountDebtSegment(
                order.Id, item.Id, 1, item.Quantity, 1)).ToArray(), changes)
                .GroupBy(value => RequireItemId(value))
                .ToDictionary(group => group.Key, group => checked((int)group.Sum(value => (long)value.Count)));
            var gross = Keep(order, roots.SelectMany(item => AccountDebtMath.CreateLine(
                order.Id, item.Id, item.Quantity, money.ToMinor(item.ItemTotal), 0)).ToArray(), changes)
                .GroupBy(value => RequireItemId(value))
                .ToDictionary(group => group.Key, group => money.ToMajor(group.Sum(value => value.TotalMinor)));
            result.AddRange(roots.Where(item => quantities.GetValueOrDefault(item.Id) > 0)
                .Select(item => new ReportedSalesLine(item, quantities[item.Id], gross.GetValueOrDefault(item.Id))));
        }
        return result;
    }

    internal static List<ZReportProductTypeDto> ByProductType(IReadOnlyList<ReportedSalesLine> lines) =>
        lines.Where(line => line.Item.Product is not null)
            .GroupBy(line => line.Item.Product!.Type)
            .Select(group => new ZReportProductTypeDto
            {
                ProductType = group.Key.ToString(),
                ItemCount = group.Sum(line => line.Quantity),
                TotalAmount = group.Sum(line => line.GrossRevenue)
            }).OrderByDescending(value => value.TotalAmount).ToList();

    internal static List<ZReportTopItemDto> TopItems(IReadOnlyList<ReportedSalesLine> lines, int count) =>
        lines.GroupBy(line => line.Item.ProductName)
            .Select(group => new ZReportTopItemDto
            {
                ProductName = group.Key,
                QuantitySold = group.Sum(line => line.Quantity),
                TotalRevenue = group.Sum(line => line.GrossRevenue)
            }).OrderByDescending(value => value.QuantitySold).Take(count).ToList();

    private static bool HasRemoval(IReadOnlyList<OrderAmendment> amendments)
    {
        try
        {
            return amendments.Any(amendment => OrderAmendmentJson
                .Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson)
                .Any(change => change.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace));
        }
        catch (JsonException)
        {
            throw new ConflictException("The report's removed-unit evidence requires reconciliation.");
        }
    }

    private static Guid RequireItemId(AccountDebtSegment segment) => segment.OrderItemId
        ?? throw new ConflictException("The report's unit scope is missing its item identity.");

    private static IReadOnlyList<AccountDebtSegment> Keep(
        Order order, IReadOnlyList<AccountDebtSegment> segments, IReadOnlyList<OrderAmendment> amendments) =>
        AccountDebtAmendmentProjection.ExcludeVoidedUnits([order], segments, amendments);
}

internal sealed record ReportedSalesLine(OrderItem Item, int Quantity, decimal GrossRevenue);
