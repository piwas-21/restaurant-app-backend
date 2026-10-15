using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed class OrderPreparationService(
    ApplicationDbContext context,
    IOptions<OrderWorkflowSettings> workflow) : IOrderPreparationService
{
    private readonly int _fallbackMinutes = workflow.Value.ConfirmedPreparationMinutes;

    public async Task<int> ResolveEffectiveMinutesAsync(
        Order order, int? authorizedOverrideMinutes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        await FreezeMissingSnapshotsAsync(order, cancellationToken);
        if (authorizedOverrideMinutes.HasValue)
            return authorizedOverrideMinutes.Value;
        if (order.EffectivePreparationMinutes.HasValue)
            return Math.Max(0, order.EffectivePreparationMinutes.Value);

        var knownPreparationTimes = order.Items
            .Where(item => item.PreparationTimeMinutesSnapshot.HasValue)
            .Select(item => Math.Max(0, item.PreparationTimeMinutesSnapshot!.Value))
            .ToList();
        return knownPreparationTimes.Count > 0
            ? knownPreparationTimes.Max()
            : _fallbackMinutes;
    }

    public async Task<int> ApplyPendingApprovalAsync(
        Order order, int? authorizedOverrideMinutes, CancellationToken cancellationToken)
    {
        var minutes = await ResolveEffectiveMinutesAsync(order, authorizedOverrideMinutes, cancellationToken);
        order.EffectivePreparationMinutes = minutes;
        order.ExpectedReadyAt = null;
        return minutes;
    }

    public async Task<int> ApplyAcceptedAsync(
        Order order, int? authorizedOverrideMinutes, DateTime acceptedAtUtc,
        CancellationToken cancellationToken)
    {
        var minutes = await ResolveEffectiveMinutesAsync(order, authorizedOverrideMinutes, cancellationToken);
        order.EffectivePreparationMinutes = minutes;
        order.ExpectedReadyAt = acceptedAtUtc.AddMinutes(minutes);
        return minutes;
    }

    public Task<int> ApplyForNewOrderAsync(
        Order order, DateTime acceptedAtUtc, CancellationToken cancellationToken) =>
        order.Status == OrderStatus.Confirmed
            ? ApplyAcceptedAsync(order, null, acceptedAtUtc, cancellationToken)
            : ApplyPendingApprovalAsync(order, null, cancellationToken);

    public async Task ApplyAcceptedOnKitchenReleaseAsync(
        Order order, DateTime releasedAtUtc, CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.Confirmed || order.ExpectedReadyAt.HasValue)
            return;

        var minutes = await ApplyAcceptedAsync(order, null, releasedAtUtc, cancellationToken);
        order.EstimatedDeliveryTime = releasedAtUtc.AddMinutes(minutes);
    }

    private async Task FreezeMissingSnapshotsAsync(Order order, CancellationToken cancellationToken)
    {
        var orderEntry = context.Entry(order);
        var itemsEntry = orderEntry.Collection(candidate => candidate.Items);
        if (orderEntry.State != EntityState.Detached
            && order.Items.Count == 0
            && !itemsEntry.IsLoaded)
        {
            await itemsEntry.LoadAsync(cancellationToken);
        }

        var missing = order.Items.Where(item => !item.PreparationTimeMinutesSnapshot.HasValue).ToList();
        if (missing.Count == 0)
            return;

        var productIds = missing.Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value).Distinct().ToArray();
        var productMinutes = productIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await context.Products.AsNoTracking()
                .Where(product => productIds.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, product => product.PreparationTimeMinutes,
                    cancellationToken);

        var menuIds = missing.Where(item => item.MenuId.HasValue)
            .Select(item => item.MenuId!.Value).Distinct().ToArray();
        var menuMinutes = menuIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await context.MenuItems.AsNoTracking()
                .Where(menuItem => menuIds.Contains(menuItem.MenuId))
                .GroupBy(menuItem => menuItem.MenuId)
                .Select(group => new
                {
                    MenuId = group.Key,
                    Minutes = group.Max(menuItem => menuItem.Product.PreparationTimeMinutes)
                })
                .ToDictionaryAsync(row => row.MenuId, row => row.Minutes, cancellationToken);

        foreach (var item in missing)
        {
            if (item.ProductId.HasValue && productMinutes.TryGetValue(item.ProductId.Value, out var product))
                item.PreparationTimeMinutesSnapshot = product;
            else if (item.MenuId.HasValue && menuMinutes.TryGetValue(item.MenuId.Value, out var menu))
                item.PreparationTimeMinutesSnapshot = menu;
        }
    }
}
