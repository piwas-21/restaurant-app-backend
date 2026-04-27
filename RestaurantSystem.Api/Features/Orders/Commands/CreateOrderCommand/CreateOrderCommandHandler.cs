using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    private readonly IOrderEventService _orderEventService;
    private readonly IOrderMappingService _mappingService;
    private readonly IFidelityPointsService _fidelityPointsService;
    private readonly ICustomerDiscountService _customerDiscountService;
    private readonly ITaxConfigurationService _taxConfigurationService;
    private readonly IEmailService _emailService;
    private readonly EmailSettings _emailSettings;

    public CreateOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderEventService orderEventService,
        IOrderMappingService mappingService,
        IFidelityPointsService fidelityPointsService,
        ICustomerDiscountService customerDiscountService,
        ITaxConfigurationService taxConfigurationService,
        IEmailService emailService,
        IOptions<EmailSettings> emailSettings,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderEventService = orderEventService;
        _mappingService = mappingService;
        _fidelityPointsService = fidelityPointsService;
        _customerDiscountService = customerDiscountService;
        _taxConfigurationService = taxConfigurationService;
        _emailService = emailService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ApiResponse<OrderDto>> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Generate order number
            var orderNumber = await GenerateOrderNumber(cancellationToken);

            var userId = command.UserId ?? _currentUserService.UserId;

            // Create order
            var order = new Order
            {
                OrderNumber = orderNumber,
                UserId = command.UserId ?? _currentUserService.UserId,
                CustomerName = command.CustomerName,
                CustomerEmail = command.CustomerEmail,
                CustomerPhone = command.CustomerPhone,
                Type = command.Type,
                TableNumber = command.TableNumber,
                PromoCode = command.PromoCode,
                HasUserLimitDiscount = command.HasUserLimitDiscount,
                UserLimitAmount = command.UserLimitAmount,
                IsFocusOrder = command.IsFocusOrder,
                Priority = command.Priority,
                FocusReason = command.FocusReason,
                FocusedAt = command.IsFocusOrder ? DateTime.UtcNow : null,
                FocusedBy = command.IsFocusOrder ? _currentUserService.UserId?.ToString() : null,
                Notes = command.Notes,
                OrderDate = DateTime.UtcNow,
                Tip = command.Tip,
                // Auto-confirm Dine-in orders, keep others as Pending
                Status = command.Type == OrderType.DineIn ? OrderStatus.Confirmed : OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };


            if (command.Type == OrderType.Delivery)
            {
                var orderAddress = await CreateOrderAddress(command.DeliveryAddress, order.Id, userId, cancellationToken);

                if (orderAddress == null)
                {
                    return ApiResponse<OrderDto>.Failure("Delivery address is required for delivery orders");
                }

                order.DeliveryAddress = orderAddress;
            }



            _context.Orders.Add(order);

            // Process order items and calculate totals
            foreach (var itemDto in command.Items)
            {

                if (itemDto.MenuId.HasValue)
                {
                    var menu = await _context.Menus
                        .Include(p => p.MenuItems)
                        .FirstOrDefaultAsync(p => p.Id == itemDto.MenuId && !p.IsDeleted, cancellationToken);

                    if (menu == null)
                    {
                        return ApiResponse<OrderDto>.Failure($"Menu {itemDto.MenuId} not found");
                    }

                    decimal unitPrice = menu.BasePrice;
                    string? variationName = null;

                    var orderItem = new OrderItem
                    {
                        ProductId = itemDto.ProductId,
                        ProductVariationId = itemDto.ProductVariationId,
                        MenuId = itemDto.MenuId,
                        ProductName = menu.Name,
                        VariationName = variationName,
                        Quantity = itemDto.Quantity,
                        UnitPrice = unitPrice,
                        ItemTotal = (unitPrice * itemDto.Quantity) + itemDto.CustomizationPrice,
                        SpecialInstructions = itemDto.SpecialInstructions,
                        IngredientQuantitiesJson = itemDto.IngredientQuantities != null ? JsonSerializer.Serialize(itemDto.IngredientQuantities) : null,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };


                    order.Items.Add(orderItem);
                }
                else if (itemDto.ProductId.HasValue)
                {
                    await CreateOrderItemRecursive(order, itemDto, null, cancellationToken);
                }
            }

            // Calculate subTotal from all added items
            decimal subTotal = order.Items.Sum(i => i.ItemTotal);

            // itemsTotal = sum of all item prices (what customer pays for items)
            decimal itemsTotal = subTotal;

            // If basket values are provided, use them directly to ensure consistency
            // Otherwise, calculate them as before
            if (command.BasketSubTotal.HasValue && command.BasketTax.HasValue &&
                command.BasketTotal.HasValue)
            {
                // Use pre-calculated basket values
                order.SubTotal = command.BasketSubTotal.Value;
                order.Tax = command.BasketTax.Value;
                order.Discount = command.BasketDiscount ?? 0;
                order.CustomerDiscountAmount = command.BasketCustomerDiscount ?? 0;

                _logger.LogInformation("Using pre-calculated basket values for order: SubTotal={SubTotal}, Tax={Tax}, Discount={Discount}, CustomerDiscount={CustomerDiscount}",
                    order.SubTotal, order.Tax, order.Discount, order.CustomerDiscountAmount);
            }
            else
            {
                // Calculate values as before (legacy path)
                // REFACTORED TAX FLOW: Tax is extracted from item prices for display only
                // It does NOT affect the final customer payment
                // Example: Product 16.90 → Tax extracted 0.44 → SubTotal shown 16.46 → Customer pays 16.90

                // Calculate tax on items total - this is for display/bills only
                order.Tax = await CalculateTax(itemsTotal, command.Type, cancellationToken);

                // SubTotal = items total minus the extracted tax (for display purposes)
                order.SubTotal = itemsTotal - order.Tax;

                // DeliveryFee is added to final price
                order.DeliveryFee = command.Type == OrderType.Delivery ? CalculateDeliveryFee() : 0;

                // Apply user discount (calculated on items total, before tax extraction)
                if (command.HasUserLimitDiscount && itemsTotal >= command.UserLimitAmount)
                {
                    var user = await _context.Users.FindAsync(userId);
                    if (user != null && user.IsDiscountActive)
                    {
                        order.DiscountPercentage = user.DiscountPercentage;
                        // Discount applies to items total (before tax extraction)
                        order.Discount = itemsTotal * (user.DiscountPercentage / 100);
                    }
                }

                // Apply customer-specific discount if available
                if (userId.HasValue)
                {
                    var customerDiscount = await _customerDiscountService.FindBestApplicableDiscountAsync(
                        userId.Value,
                        itemsTotal,
                        cancellationToken);

                    if (customerDiscount != null)
                    {
                        // Discount calculated on items total (before tax extraction)
                        var discountAmount = _customerDiscountService.CalculateDiscountAmount(customerDiscount, itemsTotal);
                        order.CustomerDiscountAmount = discountAmount;

                        // Only apply usage tracking for individual customer discounts, not group discounts
                        // Group discounts are mapped with temporary IDs that don't exist in CustomerDiscountRules
                        var isIndividualDiscount = await _context.CustomerDiscountRules
                            .AnyAsync(d => d.Id == customerDiscount.Id, cancellationToken);

                        if (isIndividualDiscount)
                        {
                            // Only set the FK reference for individual discounts to avoid constraint violation
                            order.CustomerDiscountRuleId = customerDiscount.Id;
                            await _customerDiscountService.ApplyDiscountAsync(customerDiscount.Id, cancellationToken);
                        }

                        _logger.LogInformation("Applied customer discount {DiscountName} of ${Amount} to order",
                            customerDiscount.Name, discountAmount);
                    }
                }
            }
            // Handle fidelity points redemption moved to after order save to prevent FK violation

            // Calculate fidelity points to earn for this order
            if (userId.HasValue)
            {
                var pointsToEarn = await _fidelityPointsService.CalculatePointsForOrderAsync(itemsTotal, cancellationToken);
                order.FidelityPointsEarned = pointsToEarn;

                _logger.LogInformation("Order will earn {Points} fidelity points", pointsToEarn);
            }

            // Calculate total - use basket total if provided, otherwise calculate
            if (command.BasketTotal.HasValue)
            {
                // Use pre-calculated basket total
                order.Total = command.BasketTotal.Value;
                _logger.LogInformation("Using pre-calculated basket total: {Total}", order.Total);
            }
            else
            {
                // Calculate total: Items + DeliveryFee - Discounts - FidelityDiscount
                // NOTE: Tax is NOT added to total - it's extracted from items and shown for display only
                var totalBeforeFidelity = itemsTotal + order.DeliveryFee - order.Discount - order.CustomerDiscountAmount;

                // Calculate total and apply special rounding for discounted customers
                var calculatedTotal = totalBeforeFidelity - order.FidelityPointsDiscount;
                bool hasActiveDiscount = PriceRoundingUtility.HasActiveDiscount(order.CustomerDiscountAmount + order.Discount);
                order.Total = PriceRoundingUtility.ApplySpecialRounding(calculatedTotal, hasActiveDiscount);
            }

            // Process payments
            foreach (var paymentDto in command.Payments)
            {
                var payment = new OrderPayment
                {
                    PaymentMethod = paymentDto.PaymentMethod,
                    Amount = paymentDto.Amount,
                    Status = PaymentStatus.Pending,
                    TransactionId = paymentDto.TransactionId,
                    ReferenceNumber = paymentDto.ReferenceNumber,
                    CardLastFourDigits = paymentDto.CardLastFourDigits,
                    CardType = paymentDto.CardType,
                    PaymentGateway = paymentDto.PaymentGateway,
                    PaymentNotes = paymentDto.PaymentNotes,
                    PaymentDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };

                // Only mark non-Cash payments as completed immediately
                // Cash payments remain Pending until explicitly completed via AddPaymentToOrder
                if (payment.PaymentMethod != PaymentMethod.Cash)
                {
                    payment.Status = PaymentStatus.Completed;
                    // Here you would integrate with payment gateways
                }

                order.Payments.Add(payment);
            }

            // Calculate totalPaid from only completed payments
            // Cash payments created with Pending status should not count until explicitly completed
            decimal totalPaid = order.Payments.Where(p => p.Status == PaymentStatus.Completed).Sum(p => p.Amount);

            // Update payment summary
            order.TotalPaid = totalPaid;
            order.RemainingAmount = order.Total - totalPaid;

            // Update payment status with tolerance for floating point precision in financial calculations
            // Use 0.01 (1 cent) as tolerance to handle rounding errors
            const decimal tolerance = 0.01m;
            if (order.RemainingAmount <= tolerance)
            {
                // If remaining is within tolerance of zero or negative, it's fully paid or overpaid
                order.PaymentStatus = order.RemainingAmount < -tolerance ? PaymentStatus.Overpaid : PaymentStatus.Completed;
            }
            else if (totalPaid > 0)
            {
                order.PaymentStatus = PaymentStatus.PartiallyPaid;
            }
            else
            {
                order.PaymentStatus = PaymentStatus.Pending;
            }

            // Add initial status history
            // Add order status history
            var statusHistory = new OrderStatusHistory
            {
                FromStatus = OrderStatus.Pending,
                ToStatus = order.Status,
                Notes = command.Type == OrderType.DineIn ? "Order created and auto-confirmed (Dine-in)" : "Order created",
                ChangedAt = DateTime.UtcNow,
                ChangedBy = _currentUserService.GetAuditIdentifier(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            order.StatusHistory.Add(statusHistory);

            // Calculate estimated delivery time
            if (command.Type == OrderType.Delivery)
            {
                order.EstimatedDeliveryTime = DateTime.UtcNow.AddMinutes(45); // Example: 45 minutes
            }

            await _context.SaveChangesAsync(cancellationToken);

            // Handle fidelity points redemption (moved here to prevent FK violation)
            // Now that the order is saved, we can safely create the transaction referencing it
            if (userId.HasValue && command.PointsToRedeem.HasValue && command.PointsToRedeem.Value > 0)
            {
                try
                {
                    var (pointsTransaction, discountAmount) = await _fidelityPointsService.RedeemPointsAsync(
                        userId.Value,
                        order.Id, // Order now exists in DB
                        command.PointsToRedeem.Value,
                        cancellationToken);

                    order.FidelityPointsRedeemed = command.PointsToRedeem.Value;
                    order.FidelityPointsDiscount = discountAmount;

                    _logger.LogInformation("Redeemed {Points} fidelity points for ${Discount} discount on order {OrderNumber}",
                        command.PointsToRedeem.Value, discountAmount, order.OrderNumber);

                    // Save the updates to the order
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to redeem fidelity points for order {OrderNumber}", order.OrderNumber);
                    // Don't fail the order creation, just log the error
                    // The user can contact support to resolve
                }
            }

            // Award fidelity points after successful order creation
            // Award fidelity points after successful order creation ONLY if payment is completed
            // For pending payments (e.g. Cash), points will be awarded when payment is completed
            if (userId.HasValue && order.FidelityPointsEarned > 0 &&
               (order.PaymentStatus == PaymentStatus.Completed || order.PaymentStatus == PaymentStatus.Overpaid))
            {
                try
                {
                    await _fidelityPointsService.AwardPointsAsync(
                        userId.Value,
                        order.Id,
                        order.FidelityPointsEarned,
                        order.SubTotal,
                        cancellationToken);

                    _logger.LogInformation("Awarded {Points} fidelity points to user {UserId} for order {OrderNumber}",
                        order.FidelityPointsEarned, userId, order.OrderNumber);
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the order
                    _logger.LogError(ex, "Failed to award fidelity points for order {OrderNumber}, but order was created successfully",
                        order.OrderNumber);
                }
            }

            await transaction.CommitAsync(cancellationToken);

            // Map to DTO
            var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

            // Notify clients via SSE - wrap in try-catch to ensure order creation succeeds even if notification fails
            try
            {
                _logger.LogInformation("Attempting to notify clients of order creation: {OrderNumber}", order.OrderNumber);
                await _orderEventService.NotifyOrderCreated(orderDto);
                _logger.LogInformation("Successfully notified clients of order creation: {OrderNumber}", order.OrderNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify clients of order creation for {OrderNumber}, but order was created successfully",
                    order.OrderNumber);
            }

            if (order.IsFocusOrder)
            {
                try
                {
                    await _orderEventService.NotifyFocusOrderUpdate(orderDto);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to notify clients of focus order update for {OrderNumber}", order.OrderNumber);
                }
            }

            // Send order-confirmed email for Dine-in orders (auto-confirmed)
            // For other order types, email sending is handled by the /send-confirmation-email endpoint
            if (command.Type == OrderType.DineIn && !string.IsNullOrEmpty(order.CustomerEmail))
            {
                try
                {
                    await _emailService.SendOrderConfirmedEmailAsync(
                        order.CustomerEmail,
                        order.CustomerName ?? "Valued Customer",
                        order.OrderNumber,
                        order.Type.ToString(),
                        estimatedPreparationMinutes: 15); // Default 15 minutes for dine-in

                    _logger.LogInformation("Sent order-confirmed email for Dine-in order {OrderNumber} to {Email}",
                        order.OrderNumber, order.CustomerEmail);
                }
                catch (Exception emailEx)
                {
                    // Don't fail the order creation if email fails
                    _logger.LogError(emailEx, "Failed to send order-confirmed email for Dine-in order {OrderNumber}",
                        order.OrderNumber);
                }
            }

            // Reserve table for dine-in orders
            if (order.Type == OrderType.DineIn && order.TableNumber.HasValue)
            {
                try
                {
                    var tableNumber = order.TableNumber.Value.ToString();

                    // Find table by number
                    var table = await _context.Tables
                        .FirstOrDefaultAsync(t => t.TableNumber == tableNumber && t.IsActive, cancellationToken);

                    if (table != null)
                    {
                        // Check if table is already reserved
                        var now = DateTime.UtcNow;
                        var existingReservation = await _context.TableReservations
                            .FirstOrDefaultAsync(r =>
                                r.TableId == table.Id &&
                                r.IsActive &&
                                r.ReservedUntil > now,
                                cancellationToken);

                        if (existingReservation == null)
                        {
                            var reservation = new TableReservation
                            {
                                TableId = table.Id,
                                TableNumber = tableNumber,
                                OrderId = order.Id,
                                ReservedAt = now,
                                ReservedUntil = now.AddHours(2), // 2 hour default
                                IsActive = true,
                                CreatedBy = _currentUserService.GetAuditIdentifier()
                            };

                            _context.TableReservations.Add(reservation);
                            await _context.SaveChangesAsync(cancellationToken);

                            _logger.LogInformation(
                                "Table {TableNumber} reserved for order {OrderNumber} until {ReservedUntil}",
                                tableNumber, order.OrderNumber, reservation.ReservedUntil);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Table {TableNumber} is already reserved until {ReservedUntil}",
                                tableNumber, existingReservation.ReservedUntil);
                        }
                    }
                }
                catch (Exception reservationEx)
                {
                    _logger.LogError(reservationEx,
                        "Failed to create table reservation for order {OrderNumber}",
                        order.OrderNumber);
                    // Don't fail the order creation if reservation fails
                }
            }
            // NOTE: For Takeaway and Delivery orders, email sending has been moved to the explicit /send-confirmation-email endpoint
            // This prevents duplicate emails and gives the frontend control over when emails are sent

            _logger.LogInformation("Order {OrderNumber} created successfully by user {UserId}",
                order.OrderNumber, _currentUserService.UserId);

            return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Order created successfully");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating order");
            throw;
        }
    }

    private async Task<string> GenerateOrderNumber(CancellationToken cancellationToken)
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        var lastOrder = await _context.Orders
            .Where(o => o.OrderNumber.StartsWith(date))
            .OrderByDescending(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);

        int sequence = 1;
        if (lastOrder != null)
        {
            var lastSequence = lastOrder.OrderNumber.Substring(8);
            if (int.TryParse(lastSequence, out var seq))
            {
                sequence = seq + 1;
            }
        }

        return $"{date}{sequence:D4}";
    }

    private async Task<decimal> CalculateTax(decimal subTotal, OrderType orderType, CancellationToken cancellationToken)
    {
        return await _taxConfigurationService.CalculateTaxByOrderTypeAsync(subTotal, orderType, cancellationToken);
    }

    private decimal CalculateDeliveryFee()
    {
        return 5.00m; // Fixed delivery fee, could be dynamic based on distance
    }

    private async Task<OrderAddress?> CreateOrderAddress(
       CreateOrderDeliveryAddressDto? addressDto,
       Guid orderId,
       Guid? userId,
       CancellationToken cancellationToken)
    {
        // Case 1: Use saved address ID
        if (addressDto?.UseAddressId != null)
        {
            var savedAddress = await _context.UserAddresses
                .FirstOrDefaultAsync(a => a.Id == addressDto.UseAddressId && !a.IsDeleted, cancellationToken);

            if (savedAddress != null)
            {
                return new OrderAddress
                {
                    OrderId = orderId,
                    UserAddressId = savedAddress.Id,
                    Label = savedAddress.Label,
                    AddressLine1 = savedAddress.AddressLine1,
                    AddressLine2 = savedAddress.AddressLine2,
                    City = savedAddress.City,
                    State = savedAddress.State,
                    PostalCode = savedAddress.PostalCode,
                    Country = savedAddress.Country,
                    Phone = savedAddress.Phone,
                    Latitude = savedAddress.Latitude,
                    Longitude = savedAddress.Longitude,
                    DeliveryInstructions = savedAddress.DeliveryInstructions,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };
            }
        }

        // Case 2: Use provided address details
        if (addressDto != null && !string.IsNullOrEmpty(addressDto.AddressLine1))
        {
            return new OrderAddress
            {
                OrderId = orderId,
                Label = addressDto.Label ?? "Delivery Address",
                AddressLine1 = addressDto.AddressLine1,
                AddressLine2 = addressDto.AddressLine2,
                City = addressDto.City!,
                State = addressDto.State,
                PostalCode = addressDto.PostalCode!,
                Country = addressDto.Country!,
                Phone = addressDto.Phone,
                Latitude = addressDto.Latitude,
                Longitude = addressDto.Longitude,
                DeliveryInstructions = addressDto.DeliveryInstructions,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };
        }

        // Case 3: Use customer's default address if no address provided
        if (userId.HasValue)
        {
            var defaultAddress = await _context.UserAddresses
                .FirstOrDefaultAsync(a => a.UserId == userId && a.IsDefault && !a.IsDeleted, cancellationToken);

            if (defaultAddress != null)
            {
                _logger.LogInformation("Using customer's default address for order");
                return new OrderAddress
                {
                    OrderId = orderId,
                    UserAddressId = defaultAddress.Id,
                    Label = defaultAddress.Label,
                    AddressLine1 = defaultAddress.AddressLine1,
                    AddressLine2 = defaultAddress.AddressLine2,
                    City = defaultAddress.City,
                    State = defaultAddress.State,
                    PostalCode = defaultAddress.PostalCode,
                    Country = defaultAddress.Country,
                    Phone = defaultAddress.Phone,
                    Latitude = defaultAddress.Latitude,
                    Longitude = defaultAddress.Longitude,
                    DeliveryInstructions = defaultAddress.DeliveryInstructions,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };
            }
        }

        return null;
    }

    private async Task CreateOrderItemRecursive(Order order, CreateOrderItemDto itemDto, OrderItem? parentItem, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .Include(p => p.Variations)
            .FirstOrDefaultAsync(p => p.Id == itemDto.ProductId && !p.IsDeleted, cancellationToken);

        if (product == null)
        {
            throw new NotFoundException($"Product {itemDto.ProductId} not found");
        }

        decimal unitPrice;
        string? variationName = null;

        if (itemDto.UnitPrice > 0)
        {
            unitPrice = itemDto.UnitPrice;
            if (itemDto.ProductVariationId.HasValue)
            {
                var variation = product.Variations.FirstOrDefault(v => v.Id == itemDto.ProductVariationId.Value && !v.IsDeleted);
                variationName = variation?.Name;
            }
        }
        else
        {
            unitPrice = product.BasePrice;
            if (itemDto.ProductVariationId.HasValue)
            {
                var variation = product.Variations.FirstOrDefault(v => v.Id == itemDto.ProductVariationId.Value && !v.IsDeleted);
                if (variation != null)
                {
                    unitPrice += variation.PriceModifier;
                    variationName = variation.Name;
                }
            }
        }

        var orderItem = new OrderItem
        {
            ProductId = itemDto.ProductId,
            ProductVariationId = itemDto.ProductVariationId,
            MenuId = itemDto.MenuId,
            ProductName = product.Name,
            VariationName = variationName,
            Quantity = itemDto.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = (unitPrice * itemDto.Quantity) + itemDto.CustomizationPrice,
            SpecialInstructions = itemDto.SpecialInstructions,
            IngredientQuantitiesJson = itemDto.IngredientQuantities != null ? JsonSerializer.Serialize(itemDto.IngredientQuantities) : null,
            ParentOrderItem = parentItem,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        order.Items.Add(orderItem);

        // Only add to subtotal if it's a root item (children are included in parent price usually, or handled separately)
        // If child items have price 0, it doesn't matter. If they have price, we should check logic.
        // For Menu bundles, parent has full price, children have 0 or extra price.
        // If children have extra price, it should be added to order total.
        // However, in our Basket logic, we summed everything up.
        // Let's assume ItemTotal is correct for each item.
        // If parentItem is null, it's a root item.
        // If parentItem is NOT null, it's a child.
        // We should add ALL ItemTotals to the order subtotal?
        // Yes, because BasketItem.ItemTotal for child items represents the EXTRA cost (e.g. +$2 for large drink).
        // And Parent BasketItem.ItemTotal represents base price.
        // So summing all ItemTotals is correct.

        // Wait, I need to pass subTotal back or update it.
        // Since I can't pass ref easily in async, I'll assume the caller calculates subTotal by summing order.Items.ItemTotal at the end?
        // No, the caller loop does `subTotal += orderItem.ItemTotal`.
        // I should probably return the created item or add to a list.
        // The method adds to `order.Items`.
        // I should update the caller to calculate subTotal AFTER all items are added.

        if (itemDto.ChildItems != null)
        {
            foreach (var childDto in itemDto.ChildItems)
            {
                await CreateOrderItemRecursive(order, childDto, orderItem, cancellationToken);
            }
        }
    }
}
