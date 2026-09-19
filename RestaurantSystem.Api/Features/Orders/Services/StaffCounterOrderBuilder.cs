using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Builds the staff counter order with the same catalogue and pricing services as checkout.</summary>
public sealed class StaffCounterOrderBuilder : IStaffCounterOrderBuilder
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPreferredLanguageCapture _languages;
    private readonly IOrderFactory _orderFactory;
    private readonly IOrderItemFactory _itemFactory;
    private readonly IStaffCounterOrderPricing _serverPricing;
    private readonly IOrderPricingService _pricing;
    private readonly IOrderPaymentBuilder _payments;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly ITableIdentityResolver _tables;

    [SuppressMessage("Maintainability", "S107:Methods should not have too many parameters",
        Justification = "These DI-only collaborators are the existing order construction pipeline; a parameter object or service locator would conceal required pricing and fidelity dependencies.")]
    public StaffCounterOrderBuilder(
        ApplicationDbContext context, ICurrentUserService currentUser,
        IPreferredLanguageCapture languages, IOrderFactory orderFactory,
        IOrderItemFactory itemFactory, IStaffCounterOrderPricing serverPricing,
        IOrderPricingService pricing,
        IOrderPaymentBuilder payments, IOrderFidelityCoordinator fidelity,
        ITableIdentityResolver tables)
    {
        _context = context;
        _currentUser = currentUser;
        _languages = languages;
        _orderFactory = orderFactory;
        _itemFactory = itemFactory;
        _serverPricing = serverPricing;
        _pricing = pricing;
        _payments = payments;
        _fidelity = fidelity;
        _tables = tables;
    }

    public async Task<StaffCounterOrderBuild> BuildAsync(
        StaffCounterOrderRequest request, bool releaseToKitchen, CancellationToken cancellationToken)
    {
        // Staff counter orders do not expose the customer checkout's redemption flow. Reject a
        // positive request before customer lookup, pricing, or any order mutation.
        if (request.PointsToRedeem is > 0)
        {
            throw new BadRequestException("Points redemption is not supported for staff counter orders.");
        }

        var target = await ResolveTableTargetAsync(request, cancellationToken);
        var customer = await ResolveCustomerAsync(request.EffectiveCustomerUserId, cancellationToken);
        var customerId = customer?.Id;
        var pricedItems = await _serverPricing.PriceAsync(request.Items, cancellationToken);
        var legacy = ToLegacyCommand(request, customer, pricedItems, target?.Identity.Number);
        var language = await _languages.ForUserAsync(customerId, cancellationToken);
        var draft = await _orderFactory.CreateAsync(legacy, customerId, language, cancellationToken);

        if (draft.IsFailed)
        {
            throw new BadRequestException(draft.Error);
        }

        var order = draft.Order;
        AssignTableIdentity(order, target);
        // The factory allocates the aggregate id before building delivery-address children, so the
        // operation ledger and every child FK reference the same order before the first save.
        foreach (var item in legacy.Items)
        {
            var error = await _itemFactory.AddItemAsync(
                order, item, itemsAreServerPriced: true, cancellationToken, allowStaffPrices: false);
            if (error != null)
            {
                throw new BadRequestException(error);
            }
        }

        var itemsTotal = order.Items.Sum(item => item.ItemTotal);
        await _pricing.ApplyAsync(order, itemsTotal, legacy, customerId, cancellationToken);
        await _fidelity.CalculatePointsToEarnAsync(order, itemsTotal, customerId, cancellationToken);
        _payments.UpdatePaymentSummary(order);

        var initialStatus = order.Status;
        ApplyReleaseState(order, releaseToKitchen, draft);
        order.Version = 1;
        order.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = initialStatus,
            ToStatus = order.Status,
            Notes = releaseToKitchen
                ? "Staff counter order created and released to kitchen"
                : "Staff counter order created and held for release",
            ChangedAt = draft.Now,
            ChangedBy = draft.AuditId,
            CreatedAt = draft.Now,
            CreatedBy = draft.AuditId
        });

        return new StaffCounterOrderBuild(order, legacy, customerId);
    }

    private async Task<TableOrderTarget?> ResolveTableTargetAsync(
        StaffCounterOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Type != OrderType.DineIn)
        {
            return null;
        }

        if (!request.ServiceSessionId.HasValue)
        {
            throw new BadRequestException(
                "A dine-in staff order requires an open table service session.",
                ErrorCodes.TableServiceSessionRequired);
        }

        var session = await _context.TableServiceSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == request.ServiceSessionId.Value,
                cancellationToken);
        if (session is null)
        {
            throw new NotFoundException(
                "The open table service session was not found.", ErrorCodes.TableServiceSessionNotFound);
        }

        if (session.Status != TableServiceSessionStatus.Open)
        {
            throw new BadRequestException(
                "The table service session is no longer open.", ErrorCodes.TableServiceSessionStale);
        }

        if (request.TableNumber.HasValue
            && request.TableNumber != session.TableNumber)
        {
            throw new BadRequestException(
                "The table service session does not match this dine-in order.",
                ErrorCodes.TableServiceSessionAmbiguous);
        }

        if (session.TableId.HasValue && request.TableId.HasValue
            && request.TableId != session.TableId)
        {
            throw new BadRequestException(
                "The table service session does not match this dine-in order.",
                ErrorCodes.TableServiceSessionAmbiguous);
        }

        var table = await _tables.ResolveActiveAsync(
            request.TableId ?? session.TableId,
            request.TableNumber ?? session.TableNumber,
            cancellationToken);
        if (session.TableNumber != table.Number
            || (session.TableId.HasValue && session.TableId != table.Id))
        {
            throw new BadRequestException(
                "The table service session does not match this dine-in order.",
                ErrorCodes.TableServiceSessionAmbiguous);
        }

        return new TableOrderTarget(table, session.Id);
    }

    private static void AssignTableIdentity(Order order, TableOrderTarget? target)
    {
        if (target is null)
        {
            return;
        }

        order.TableId = target.Identity.Id;
        order.TableNumber = target.Identity.Number;
        order.TableLabel = target.Identity.Label;
        order.ServiceSessionId = target.ServiceSessionId;
    }

    private async Task<ApplicationUser?> ResolveCustomerAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        if (!customerId.HasValue)
        {
            return null;
        }

        if (!_currentUser.IsAdmin && _currentUser.Role != UserRole.Cashier)
        {
            throw new ForbiddenException("Only an admin or cashier may place an order on behalf of a customer.");
        }

        var customer = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == customerId.Value && !user.IsDeleted, cancellationToken);
        if (customer == null)
        {
            throw new NotFoundException("Customer account not found.");
        }

        if (customer.Role != UserRole.Customer)
        {
            throw new ForbiddenException("The selected account is not a customer account.");
        }

        return customer;
    }

    private static CreateOrderCommand ToLegacyCommand(
        StaffCounterOrderRequest request, ApplicationUser? customer,
        List<CreateOrderItemDto> pricedItems, int? tableNumber) => new()
        {
            CustomerName = customer == null
                ? request.CustomerName
                : $"{customer.FirstName} {customer.LastName}".Trim(),
            CustomerEmail = customer?.Email ?? request.CustomerEmail,
            CustomerPhone = customer?.PhoneNumber ?? request.CustomerPhone,
            Type = request.Type,
            TableNumber = tableNumber,
            DeliveryAddress = request.DeliveryAddress,
            PromoCode = request.PromoCode,
            PointsToRedeem = request.PointsToRedeem,
            Tip = request.Tip ?? 0m,
            Notes = request.Notes,
            Items = pricedItems,
            ItemsAreServerPriced = true,
            Payments = [],
        };

    private sealed record TableOrderTarget(TableIdentity Identity, Guid? ServiceSessionId);

    private static void ApplyReleaseState(Order order, bool releaseToKitchen, OrderDraft draft)
    {
        order.IsKitchenReleased = releaseToKitchen;
        order.KitchenReleasedAt = releaseToKitchen ? draft.Now : null;
        order.KitchenReleasedBy = releaseToKitchen ? draft.AuditId : null;

        if (releaseToKitchen && order.Status == OrderStatus.Pending)
        {
            order.Status = OrderStatus.Confirmed;
        }
    }
}
