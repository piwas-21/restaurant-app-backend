using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderTableReservationService : IOrderTableReservationService
{
    // Default reservation window. Promote to OrderSettings:DineInReservationDuration
    // if this needs to vary per deployment.
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(2);

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<OrderTableReservationService> _logger;

    public OrderTableReservationService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<OrderTableReservationService> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task ReserveForDineInAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.Type != OrderType.DineIn || !order.TableNumber.HasValue)
        {
            return;
        }

        try
        {
            var tableNumber = order.TableNumber.Value.ToString();

            var table = await _context.Tables
                .FirstOrDefaultAsync(t => t.TableNumber == tableNumber && t.IsActive, cancellationToken);

            if (table == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var existingReservation = await _context.TableReservations
                .FirstOrDefaultAsync(
                    r => r.TableId == table.Id && r.IsActive && r.ReservedUntil > now,
                    cancellationToken);

            if (existingReservation != null)
            {
                _logger.LogWarning(
                    "Table {TableNumber} is already reserved until {ReservedUntil}",
                    tableNumber, existingReservation.ReservedUntil);
                return;
            }

            var reservation = new TableReservation
            {
                TableId = table.Id,
                TableNumber = tableNumber,
                OrderId = order.Id,
                ReservedAt = now,
                ReservedUntil = now + DefaultDuration,
                IsActive = true,
                CreatedBy = _currentUserService.GetAuditIdentifier(),
            };

            _context.TableReservations.Add(reservation);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Table {TableNumber} reserved for order {OrderNumber} until {ReservedUntil}",
                tableNumber, order.OrderNumber, reservation.ReservedUntil);
        }
        catch (Exception ex)
        {
            // Best-effort: order creation must not fail because table
            // reservation did. Preserved verbatim from the inline block.
            _logger.LogError(
                ex, "Failed to create table reservation for order {OrderNumber}", order.OrderNumber);
        }
    }
}
