using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.UpdateReservationCommand;

public record UpdateReservationCommand(Guid ReservationId, UpdateReservationDto ReservationData)
    : ICommand<ApiResponse<ReservationDto>>;

public class UpdateReservationCommandHandler : ICommandHandler<UpdateReservationCommand, ApiResponse<ReservationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<UpdateReservationCommandHandler> _logger;

    public UpdateReservationCommandHandler(
        ApplicationDbContext context,
        IEmailService emailService,
        ILogger<UpdateReservationCommandHandler> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ApiResponse<ReservationDto>> Handle(UpdateReservationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var reservation = await _context.Reservations
                .Include(r => r.Table)
                .FirstOrDefaultAsync(r => r.Id == command.ReservationId, cancellationToken);

            if (reservation == null)
            {
                return ApiResponse<ReservationDto>.Failure("Reservation not found");
            }

            var data = command.ReservationData;
            var previousStatus = reservation.Status;

            // Validate table exists and is active
            var table = await _context.Tables
                .FirstOrDefaultAsync(t => t.Id == data.TableId && t.IsActive, cancellationToken);

            if (table == null)
            {
                return ApiResponse<ReservationDto>.Failure("Table not found or inactive");
            }

            // Validate table capacity
            if (table.MaxGuests < data.NumberOfGuests)
            {
                return ApiResponse<ReservationDto>.Failure($"Table {table.TableNumber} can only accommodate {table.MaxGuests} guests");
            }

            // Check for time slot conflicts (excluding current reservation)
            var hasConflict = await _context.Reservations
                .AnyAsync(r =>
                    r.Id != command.ReservationId &&
                    r.TableId == data.TableId &&
                    r.ReservationDate.Date == data.ReservationDate.Date &&
                    (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
                    ((r.StartTime < data.EndTime && r.EndTime > data.StartTime)),
                    cancellationToken);

            if (hasConflict)
            {
                return ApiResponse<ReservationDto>.Failure($"Table {table.TableNumber} is not available for the selected time slot");
            }

            // Update reservation
            reservation.CustomerName = data.CustomerName;
            reservation.CustomerEmail = data.CustomerEmail;
            reservation.CustomerPhone = data.CustomerPhone;
            reservation.TableId = data.TableId;
            reservation.ReservationDate = data.ReservationDate;
            reservation.StartTime = data.StartTime;
            reservation.EndTime = data.EndTime;
            reservation.NumberOfGuests = data.NumberOfGuests;
            reservation.Status = data.Status;
            reservation.SpecialRequests = data.SpecialRequests;
            reservation.Notes = data.Notes;

            await _context.SaveChangesAsync(cancellationToken);

            // Send approval email if status changed to Confirmed
            if (previousStatus != ReservationStatus.Confirmed && data.Status == ReservationStatus.Confirmed)
            {
                try
                {
                    await _emailService.SendReservationApprovedEmailAsync(
                        reservation.CustomerEmail,
                        reservation.CustomerName,
                        table.TableNumber,
                        reservation.ReservationDate,
                        reservation.StartTime,
                        reservation.EndTime,
                        reservation.NumberOfGuests,
                        reservation.SpecialRequests,
                        reservation.Notes);

                    _logger.LogInformation("Approval email sent for reservation {ReservationId}", reservation.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send approval email for reservation {ReservationId}", reservation.Id);
                    // Don't fail the update if email fails
                }
            }

            var reservationDto = new ReservationDto
            {
                Id = reservation.Id,
                CustomerId = reservation.CustomerId,
                CustomerName = reservation.CustomerName,
                CustomerEmail = reservation.CustomerEmail,
                CustomerPhone = reservation.CustomerPhone,
                TableId = reservation.TableId,
                TableNumber = table.TableNumber,
                ReservationDate = reservation.ReservationDate,
                StartTime = reservation.StartTime,
                EndTime = reservation.EndTime,
                NumberOfGuests = reservation.NumberOfGuests,
                Status = reservation.Status,
                SpecialRequests = reservation.SpecialRequests,
                Notes = reservation.Notes,
                CreatedAt = reservation.CreatedAt
            };

            _logger.LogInformation("Updated reservation {ReservationId}", reservation.Id);
            return ApiResponse<ReservationDto>.SuccessWithData(reservationDto, "Reservation updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating reservation {ReservationId}", command.ReservationId);
            return ApiResponse<ReservationDto>.Failure("Failed to update reservation");
        }
    }
}
