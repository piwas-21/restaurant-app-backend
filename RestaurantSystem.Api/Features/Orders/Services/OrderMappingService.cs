using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

public class OrderMappingService : IOrderMappingService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OrderMappingService> _logger;

    public OrderMappingService(ApplicationDbContext context, ILogger<OrderMappingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public OrderDto MapToOrderDto(Order order)
    {
        return new OrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            UserId = order.UserId,
            CustomerName = order.CustomerName,
            CustomerEmail = order.CustomerEmail,
            CustomerPhone = order.CustomerPhone,
            Type = order.Type.ToString(),
            TableNumber = order.TableNumber,
            SubTotal = order.SubTotal,
            Tax = order.Tax,
            DeliveryFee = order.DeliveryFee,
            Discount = order.Discount,
            DiscountPercentage = order.DiscountPercentage,
            CustomerDiscountAmount = order.CustomerDiscountAmount,
            Tip = order.Tip,
            Total = order.Total,
            TotalPaid = order.TotalPaid,
            RemainingAmount = order.RemainingAmount,
            IsFullyPaid = order.IsFullyPaid,
            PromoCode = order.PromoCode,
            HasUserLimitDiscount = order.HasUserLimitDiscount,
            UserLimitAmount = order.UserLimitAmount,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            IsFocusOrder = order.IsFocusOrder,
            Priority = order.Priority,
            FocusReason = order.FocusReason,
            FocusedAt = order.FocusedAt,
            FocusedBy = order.FocusedBy,
            OrderDate = order.OrderDate,
            EstimatedDeliveryTime = order.EstimatedDeliveryTime,
            ActualDeliveryTime = order.ActualDeliveryTime,
            Notes = order.Notes,
            CancellationReason = order.CancellationReason,
            DeliveryAddress = MapToDeliveryAddressDto(order.DeliveryAddress),
            Items = order.Items?.Select(MapToOrderItemDto).ToList() ?? new List<OrderItemDto>(),
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
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        };
    }

    public OrderSummaryDto MapToOrderSummaryDto(Order order)
    {
        return new OrderSummaryDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerName = order.CustomerName,
            Type = order.Type.ToString(),
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total,
            OrderDate = order.OrderDate,
            ItemCount = order.Items?.Count ?? 0,
            IsFocusOrder = order.IsFocusOrder
        };
    }

    public OrderItemDto MapToOrderItemDto(OrderItem item)
    {
        // Parse ingredient customizations
        List<OrderItemIngredientDto>? ingredientCustomizations = null;

        // Get product ingredients - either from direct Product or from Menu's first MenuItem's Product
        ICollection<ProductIngredient>? productIngredients = null;
        if (item.Product?.DetailedIngredients != null)
        {
            productIngredients = item.Product.DetailedIngredients;
        }
        else if (item.Menu?.MenuItems?.Any() == true)
        {
            // For menu items (e.g., Chief's Special), get ingredients from the menu's product
            var firstMenuItem = item.Menu.MenuItems.FirstOrDefault();
            if (firstMenuItem?.Product?.DetailedIngredients != null)
            {
                productIngredients = firstMenuItem.Product.DetailedIngredients;
            }
        }

        if (!string.IsNullOrEmpty(item.IngredientQuantitiesJson) && productIngredients != null)
        {
            try
            {
                var selectedIngredients = System.Text.Json.JsonSerializer.Deserialize<Dictionary<Guid, int>>(item.IngredientQuantitiesJson);
                if (selectedIngredients != null && selectedIngredients.Any())
                {
                    ingredientCustomizations = new List<OrderItemIngredientDto>();

                    // Map ingredient customizations
                    // Show all ingredients for kitchen (both selected and removed)
                    foreach (var ing in productIngredients)
                    {
                        if (selectedIngredients.TryGetValue(ing.Id, out var quantity))
                        {
                            // Ingredient is in the order - show it regardless of quantity
                            ingredientCustomizations.Add(new OrderItemIngredientDto
                            {
                                IngredientId = ing.Id,
                                IngredientName = ing.GlobalIngredient?.DefaultName ?? ing.Name,
                                Quantity = quantity,
                                IsRemoved = quantity == 0
                            });
                        }
                        else if (!ing.IsOptional)
                        {
                            // Required ingredient not in selection at all = removed
                            ingredientCustomizations.Add(new OrderItemIngredientDto
                            {
                                IngredientId = ing.Id,
                                IngredientName = ing.GlobalIngredient?.DefaultName ?? ing.Name,
                                Quantity = 0,
                                IsRemoved = true
                            });
                        }
                    }
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse ingredient quantities for order item {ItemId}", item.Id);
            }
        }

        // Get KitchenType from Product or Menu's first MenuItem's Product
        string? kitchenType = item.Product?.KitchenType.ToString();
        if (kitchenType == null && item.Menu?.MenuItems?.Any() == true)
        {
            var firstMenuItem = item.Menu.MenuItems.FirstOrDefault();
            kitchenType = firstMenuItem?.Product?.KitchenType.ToString();
        }

        // Map child items. A child of a bundle/combo (ProductType.Menu parent) is a bundle
        // component; a child of a regular item is a true add-on side. The Kind discriminator
        // (DTO-only, derived from the parent's product type) lets the kitchen ticket and the
        // order UI tell the two apart — they otherwise share the SideItems collection (#158).
        List<OrderItemDto>? sideItems = null;
        if (item.ChildOrderItems != null && item.ChildOrderItems.Any())
        {
            var childKind = item.Product?.Type == ProductType.Menu ? ItemKind.BundleChild : ItemKind.SideItem;
            sideItems = item.ChildOrderItems
                .Select(child =>
                {
                    var childDto = MapToOrderItemDto(child);
                    childDto.Kind = childKind;
                    return childDto;
                })
                .ToList();
        }

        return new OrderItemDto
        {
            Id = item.Id,
            ProductId = item.ProductId,
            ProductVariationId = item.ProductVariationId,
            MenuID = item.MenuId,
            ProductName = item.ProductName,
            VariationName = item.VariationName,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            ItemTotal = item.ItemTotal,
            SpecialInstructions = item.SpecialInstructions,
            KitchenType = kitchenType,
            IngredientCustomizations = ingredientCustomizations,
            SideItems = sideItems
        };
    }

    public OrderPaymentDto MapToOrderPaymentDto(OrderPayment payment)
    {
        return new OrderPaymentDto
        {
            Id = payment.Id,
            OrderId = payment.OrderId,
            PaymentMethod = payment.PaymentMethod.ToString(),
            Amount = payment.Amount,
            Status = payment.Status.ToString(),
            TransactionId = payment.TransactionId,
            PaymentDate = payment.PaymentDate,
            RefundedAmount = payment.RefundedAmount,
            RefundDate = payment.RefundDate,
            RefundReason = payment.RefundReason,
            CreatedAt = payment.CreatedAt
        };
    }

    public DeliveryAddressDto? MapToDeliveryAddressDto(OrderAddress? address)
    {
        if (address == null) return null;

        return new DeliveryAddressDto
        {
            Id = address.Id,
            OrderId = address.OrderId,
            UserAddressId = address.UserAddressId,
            Label = address.Label,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            City = address.City,
            State = address.State,
            PostalCode = address.PostalCode,
            Country = address.Country,
            Phone = address.Phone,
            Latitude = address.Latitude,
            Longitude = address.Longitude,
            DeliveryInstructions = address.DeliveryInstructions,
            FullAddress = address.GetFullAddress()
        };
    }

    public async Task<OrderDto> MapToOrderDtoAsync(Order order, CancellationToken cancellationToken = default)
    {
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

        return MapToOrderDto(order);
    }

    // Ensures a product's DetailedIngredients — and each ingredient's GlobalIngredient
    // reference, needed to resolve display names — are loaded. Each load is guarded by
    // its own IsLoaded flag so the inner GlobalIngredient loads still run when
    // DetailedIngredients was already tracked (e.g. via EF relationship fixup). That
    // closes the fixup-loaded ingredient defect (#150/#152/#153) one level deeper and
    // deduplicates the identical graph-walk the Product and Menu branches shared (#161).
    private async Task EnsureProductIngredientsLoadedAsync(Product product, CancellationToken cancellationToken)
    {
        if (!_context.Entry(product).Collection(p => p.DetailedIngredients).IsLoaded)
        {
            await _context.Entry(product).Collection(p => p.DetailedIngredients).LoadAsync(cancellationToken);
        }

        // DetailedIngredients is initialized to [] on the entity, but guard anyway to
        // match the file's existing defensive style (see MapToOrderItemDto) and stay
        // safe for manually-constructed / mocked Products.
        if (product.DetailedIngredients == null)
        {
            return;
        }

        foreach (var ing in product.DetailedIngredients)
        {
            if (!_context.Entry(ing).Reference(i => i.GlobalIngredient).IsLoaded)
            {
                await _context.Entry(ing).Reference(i => i.GlobalIngredient).LoadAsync(cancellationToken);
            }
        }
    }
}
