using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

public partial class OrderMappingService : IOrderMappingService
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderDisplayCurrencyResolver _currencyResolver;
    private readonly ILogger<OrderMappingService> _logger;

    public OrderMappingService(
        ApplicationDbContext context,
        IOrderDisplayCurrencyResolver currencyResolver,
        ILogger<OrderMappingService> logger)
    {
        _context = context;
        _currencyResolver = currencyResolver;
        _logger = logger;
    }

    public OrderDto MapToOrderDto(Order order)
    {
        // Child rows live in the SAME Items collection as their parents — a child carries
        // its parent's OrderId — so the flat list already holds the whole tree. Grouping it
        // here, rather than reading the ChildOrderItems navigation, is what makes SideItems
        // independent of how the caller happened to query (#234):
        // On AsNoTracking reads (the printer feed) the navigation was never populated and
        // there is no relationship fix-up to compensate, so SideItems came back null on every
        // order and bundle components printed as flat top-level lines. On tracked reads fix-up
        // DID populate it, but every child was still projected a second time as its own
        // top-level line, rendering it twice.
        // It also spans arbitrary bundle depth (OrderItemFactory nests grandchildren) with
        // no include chain to keep in sync. Root-only projection matches the precedent in
        // GetZReportQuery (.Where(i => i.ParentOrderItemId == null)).
        var items = order.Items ?? new List<OrderItem>();
        var childrenByParent = items
            .Where(i => i.ParentOrderItemId.HasValue)
            .ToLookup(i => i.ParentOrderItemId!.Value);

        return new OrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            ExternalOrder = ExternalOrderProjection.Map(order),
            GuestStatusToken = order.GuestStatusToken,
            UserId = order.UserId,
            CustomerName = order.CustomerName,
            CustomerEmail = order.CustomerEmail,
            CustomerPhone = order.CustomerPhone,
            Type = order.Type.ToString(),
            TableNumber = order.TableNumber,
            TableId = order.TableId,
            TableLabel = order.TableLabel ?? order.Table?.TableNumber,
            ServiceSessionId = order.ServiceSessionId,
            SubTotal = order.SubTotal,
            Tax = order.Tax,
            DeliveryFee = order.DeliveryFee,
            Discount = order.Discount,
            DiscountPercentage = order.DiscountPercentage,
            CustomerDiscountAmount = order.CustomerDiscountAmount,
            FidelityPointsEarned = order.FidelityPointsEarned,
            FidelityPointsRedeemed = order.FidelityPointsRedeemed,
            FidelityPointsDiscount = order.FidelityPointsDiscount,
            Tip = order.Tip,
            Total = order.Total,
            PayableTotal = order.BillingCreditAmount > 0 ? order.PayableTotal : null,
            BillingCreditAmount = order.BillingCreditAmount,
            TotalPaid = order.TotalPaid,
            RemainingAmount = order.PayableTotal - order.TotalPaid,
            PaymentTipMinor = order.Payments?.Where(payment => payment.Status.IsCaptured())
                .Sum(payment => payment.TipMinor - payment.RefundedTipMinor) ?? 0,
            IsFullyPaid = order.IsFullyPaid,
            IsKitchenReleased = order.IsKitchenReleased,
            KitchenReleasedAt = order.KitchenReleasedAt,
            KitchenReleasedBy = order.KitchenReleasedBy,
            PromoCode = order.PromoCode,
            HasUserLimitDiscount = order.HasUserLimitDiscount,
            UserLimitAmount = order.UserLimitAmount,
            Currency = _currencyResolver.Resolve(order),
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            Version = order.Version,
            IsFocusOrder = order.Focus is not null,
            Priority = order.Focus?.Priority,
            FocusReason = order.Focus?.Reason,
            FocusedAt = order.Focus?.FocusedAt,
            FocusedBy = order.Focus?.FocusedBy,
            OrderTypeOverrideBy = order.OrderTypeOverrideBy,
            OrderTypeOverrideItems = order.OrderTypeOverrideItems,
            OrderDate = order.OrderDate,
            EstimatedDeliveryTime = order.EstimatedDeliveryTime,
            ActualDeliveryTime = order.ActualDeliveryTime,
            Notes = order.Notes,
            PreferredLanguage = order.PreferredLanguage,
            CancellationReason = order.CancellationReason,
            DeliveryAddress = MapToDeliveryAddressDto(order.DeliveryAddress),
            Items = items
                .Where(i => !i.ParentOrderItemId.HasValue)
                .Select(i => MapOrderItem(i, childrenByParent))
                .ToList(),
            Payments = order.Payments?.Select(MapToOrderPaymentDto).ToList() ?? new List<OrderPaymentDto>(),
            StatusHistory = order.StatusHistory?.Select(sh => new OrderStatusHistoryDto
            {
                Id = sh.Id,
                FromStatus = sh.FromStatus.ToString(),
                ToStatus = sh.ToStatus.ToString(),
                Notes = sh.Notes,
                ChangedAt = sh.CreatedAt,
                ChangedBy = sh.CreatedBy
            }).ToList() ?? new List<OrderStatusHistoryDto>(),
            RoutingStates = OrderRoutingProjection.Map(order),
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        };
    }

    public OrderPaymentDto MapToOrderPaymentDto(OrderPayment payment)
    {
        return new OrderPaymentDto
        {
            Id = payment.Id,
            OrderId = payment.OrderId,
            OperationId = payment.OperationId,
            PaymentMethod = payment.PaymentMethod.ToString(),
            Amount = payment.Amount,
            TipMinor = payment.TipMinor,
            Status = payment.Status.ToString(),
            TransactionId = payment.TransactionId,
            PaymentDate = payment.PaymentDate,

            // Declared on the DTO since it was written, populated by nobody until now — so every
            // read surface saw null and could not tell a Stripe capture from cash. The cashier and
            // admin refund dialogs need exactly this to stop offering a refund the server refuses
            // (TenderCustody); a gate keyed on a field the mapper drops is a gate that never fires.
            //
            // Only this one of the mapper's five dropped fields is added. ReferenceNumber,
            // CardLastFourDigits, CardType, PaymentNotes and IsRefunded stay dropped — nothing
            // reads them, and surfacing card metadata to every order consumer is a privacy
            // decision, not a mapping oversight to fix in passing.
            PaymentGateway = payment.PaymentGateway,
            RefundedAmount = payment.RefundedAmount,
            RefundedTipMinor = payment.RefundedTipMinor,
            RefundDate = payment.RefundDate,
            RefundReason = payment.RefundReason,
            CreatedAt = payment.CreatedAt
        };
    }

    public async Task<OrderDto> MapToOrderDtoAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (!_context.Entry(order).Reference(o => o.ExternalReference).IsLoaded)
        {
            await _context.Entry(order).Reference(o => o.ExternalReference).LoadAsync(cancellationToken);
        }

        // Load related data if not already loaded
        if (!_context.Entry(order).Collection(o => o.Items).IsLoaded)
        {
            await _context.Entry(order).Collection(o => o.Items).LoadAsync(cancellationToken);
        }

        // Load Product for each item to access KitchenType and DetailedIngredients
        if (order.Items != null)
        {
            foreach (var item in order.Items)
            {
                // The FROZEN ingredient lines (S1) are what a placed order renders from; the catalog
                // loads below are the fallback for rows written before the snapshot existed. Loading
                // it here rather than relying on fix-up matters for the same reason the loads below
                // do: this path serves an order that was fetched with a narrow include chain, and a
                // missing navigation reads as "no snapshot" instead of throwing (#234's class).
                if (!_context.Entry(item).Collection(i => i.IngredientSnapshots).IsLoaded)
                {
                    await _context.Entry(item).Collection(i => i.IngredientSnapshots).LoadAsync(cancellationToken);
                }

                // Load Product for regular product items
                if (item.ProductId.HasValue)
                {
                    if (!_context.Entry(item).Reference(i => i.Product).IsLoaded)
                    {
                        await _context.Entry(item).Reference(i => i.Product).LoadAsync(cancellationToken);
                    }

                    // Load DetailedIngredients + their GlobalIngredient refs for
                    // ingredient names. Runs independently of the Product load above:
                    // when the Product is already tracked (e.g. the order was just built
                    // by OrderItemFactory in this same context), relationship fixup marks
                    // the reference loaded and a nested check would skip this — leaving
                    // ingredient customizations empty on the create-order response
                    // (#150/#152). The helper guards each load level on its own IsLoaded
                    // flag so the GlobalIngredient refs still load even when
                    // DetailedIngredients was already tracked — same class, one level
                    // deeper (#161).
                    if (item.Product != null)
                    {
                        await EnsureProductIngredientsLoadedAsync(item.Product, cancellationToken);
                    }
                }

                // Load Menu and its Product for menu items (e.g., Chief's Special)
                if (item.MenuId.HasValue)
                {
                    if (!_context.Entry(item).Reference(i => i.Menu).IsLoaded)
                    {
                        await _context.Entry(item).Reference(i => i.Menu).LoadAsync(cancellationToken);
                    }

                    // Checked independently of the Menu load above: when the Menu is
                    // already tracked (e.g. the order was just built in this same
                    // context), relationship fixup marks the reference loaded and the
                    // previously nested checks skipped these loads — leaving ingredient
                    // customizations empty on the create-order response. Issue #153,
                    // same class as the Product branch fixed in #152.
                    if (item.Menu != null && !_context.Entry(item.Menu).Collection(m => m.MenuItems).IsLoaded)
                    {
                        await _context.Entry(item.Menu).Collection(m => m.MenuItems).LoadAsync(cancellationToken);
                    }

                    // Load Product + DetailedIngredients (and their GlobalIngredient
                    // refs) for each menu item. The per-ingredient loads run through the
                    // shared helper regardless of whether DetailedIngredients was already
                    // tracked — closing the same fixup-loaded defect class one level
                    // deeper than the Menu-reference fix in #153 (#161).
                    if (item.Menu?.MenuItems != null)
                    {
                        foreach (var menuItem in item.Menu.MenuItems)
                        {
                            if (!_context.Entry(menuItem).Reference(mi => mi.Product).IsLoaded)
                            {
                                await _context.Entry(menuItem).Reference(mi => mi.Product).LoadAsync(cancellationToken);
                            }

                            if (menuItem.Product != null)
                            {
                                await EnsureProductIngredientsLoadedAsync(menuItem.Product, cancellationToken);
                            }
                        }
                    }
                }
            }
        }

        if (!_context.Entry(order).Collection(o => o.Payments).IsLoaded)
        {
            await _context.Entry(order).Collection(o => o.Payments).LoadAsync(cancellationToken);
        }

        if (!_context.Entry(order).Collection(o => o.StatusHistory).IsLoaded)
        {
            await _context.Entry(order).Collection(o => o.StatusHistory).LoadAsync(cancellationToken);
        }

        if (!_context.Entry(order).Reference(o => o.DeliveryAddress).IsLoaded)
        {
            await _context.Entry(order).Reference(o => o.DeliveryAddress).LoadAsync(cancellationToken);
        }

        if (!_context.Entry(order).Collection(o => o.RoutingStates).IsLoaded)
        {
            await _context.Entry(order).Collection(o => o.RoutingStates).LoadAsync(cancellationToken);
        }

        return MapToOrderDto(order);
    }

    // Ensures a product's DetailedIngredients are loaded, guarded by its own IsLoaded flag so the
    // load still runs when the Product reference was already tracked via EF relationship fixup —
    // the fixup-loaded ingredient defect (#150/#152/#153) — and deduplicating the identical
    // graph-walk the Product and Menu branches shared (#161).
    //
    // This is the FALLBACK path only: a line carrying a frozen snapshot (S1) never reaches the
    // catalog at all. It stays because S1 backfills nothing, so every pre-S1 line still resolves
    // its id map against the live recipe.
    //
    // The per-ingredient GlobalIngredient load that used to follow is GONE (S1). S0n stopped the
    // order line reading GlobalIngredient.DefaultName — a rename must not reword a placed order —
    // and left the load in place because dropping a level of a load graph deserved its own,
    // separately revertible change. This is that change: nothing on an order reads the navigation,
    // so the loop was one query per ingredient per order for a value no DTO carries.
    private async Task EnsureProductIngredientsLoadedAsync(Product product, CancellationToken cancellationToken)
    {
        if (!_context.Entry(product).Collection(p => p.DetailedIngredients).IsLoaded)
        {
            await _context.Entry(product).Collection(p => p.DetailedIngredients).LoadAsync(cancellationToken);
        }
    }
}
