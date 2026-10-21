using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CancelReservationCommand;

/// <summary>Signed-in cancellation. Only the email controller can attach an internal original
/// proof instead of caller ownership, and the handler revalidates that proof after waiting.</summary>
public record CancelReservationCommand(Guid ReservationId) : ICommand<ApiResponse<bool>>
{
    [BindNever, JsonIgnore]
    internal ReservationQuickActionAuthority? QuickAction { get; init; }
}

public class CancelReservationCommandHandler : ICommandHandler<CancelReservationCommand, ApiResponse<bool>>
{
    private const string NotFoundMessage = "Reservation not found";

    private readonly ApplicationDbContext _context;
    private readonly IReservationQuickActionLinks _quickActions;
    private readonly ICurrentUserService _currentUser;
    private readonly IReservationRejectedMailer _mailer;
    private readonly ILogger<CancelReservationCommandHandler> _logger;

    public CancelReservationCommandHandler(
        ApplicationDbContext context,
        IReservationQuickActionLinks quickActions,
        ICurrentUserService currentUser,
        IReservationRejectedMailer mailer,
        ILogger<CancelReservationCommandHandler> logger)
    {
        _context = context;
        _quickActions = quickActions;
        _currentUser = currentUser;
        _mailer = mailer;
        _logger = logger;
    }

    // Staff cancel any booking; a customer cancels only their own. Guest reservations
    // (CustomerId == null) are deliberately unreachable: matching a null owner against a null
    // caller would hand every guest booking to any signed-in user.
    private bool CanCurrentUserCancel(Reservation reservation) =>
        _currentUser.IsStaff
        || (_currentUser.UserId.HasValue && reservation.CustomerId == _currentUser.UserId.Value);

    public async Task<ApiResponse<bool>> Handle(CancelReservationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await ReservationWriteLock.BeginAsync(_context, cancellationToken);
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.Id == command.ReservationId, cancellationToken);

            if (command.QuickAction is { } authority
                && !authority.Allows(reservation, ReservationQuickAction.Reject, _quickActions))
                return ReservationQuickActionAuthority.Refused();

            if (reservation == null)
            {
                return ApiResponse<bool>.Failure(NotFoundMessage);
            }

            if (command.QuickAction is null && !CanCurrentUserCancel(reservation))
            {
                // Word for word the missing-reservation answer, on purpose: a distinct 403 would
                // confirm the id exists and let anyone enumerate other guests' bookings. The real
                // reason is recorded server-side only.
                _logger.LogWarning(
                    "User {UserId} denied cancellation of reservation {ReservationId} they do not own; responding as not-found",
                    _currentUser.UserId, command.ReservationId);
                return ApiResponse<bool>.Failure(NotFoundMessage);
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                return ApiResponse<bool>.Failure("Reservation is already cancelled");
            }

            if (reservation.Status == ReservationStatus.Completed)
            {
                return ApiResponse<bool>.Failure("Cannot cancel a completed reservation");
            }

            reservation.Status = ReservationStatus.Cancelled;
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await _mailer.SendAsync(reservation, cancellationToken);

            _logger.LogInformation("Cancelled reservation {ReservationId}", reservation.Id);
            return ApiResponse<bool>.SuccessWithData(true, "Reservation cancelled successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling reservation {ReservationId}", command.ReservationId);
            return ApiResponse<bool>.Failure("Failed to cancel reservation");
        }
    }
}
