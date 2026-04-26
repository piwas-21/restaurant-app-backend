using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ReleaseTableCommand;

public record ReleaseTableCommand(string TableNumber) : ICommand<ApiResponse<bool>>;

public class ReleaseTableCommandHandler : ICommandHandler<ReleaseTableCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ReleaseTableCommandHandler> _logger;

    public ReleaseTableCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<ReleaseTableCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<bool>> Handle(ReleaseTableCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;

            // Find active reservation for this table
            var reservation = await _context.TableReservations
                .FirstOrDefaultAsync(r =>
                    r.TableNumber == request.TableNumber &&
                    r.IsActive &&
                    r.ReservedUntil > now,
                    cancellationToken);

            if (reservation == null)
            {
                return ApiResponse<bool>.Failure("No active reservation found for this table");
            }

            // Release the reservation
            reservation.IsActive = false;
            reservation.ReleasedAt = now;
            reservation.ReleasedBy = _currentUserService.GetAuditIdentifier();
            reservation.ReleaseReason = "Manual";

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Table {TableNumber} manually released by user {UserId} (order: {OrderId})",
                request.TableNumber,
                _currentUserService.UserId,
                reservation.OrderId);

            return ApiResponse<bool>.SuccessWithData(true, "Table released successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing table {TableNumber}", request.TableNumber);
            return ApiResponse<bool>.Failure("Failed to release table");
        }
    }
}
