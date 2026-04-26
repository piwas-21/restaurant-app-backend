using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateReservationCommand;

public record CreateReservationCommand(CreateReservationDto ReservationData, Guid? CustomerId = null)
    : ICommand<ApiResponse<ReservationDto>>;

public class CreateReservationCommandHandler : ICommandHandler<CreateReservationCommand, ApiResponse<ReservationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<CreateReservationCommandHandler> _logger;
    private readonly EmailSettings _emailSettings;

    public CreateReservationCommandHandler(
        ApplicationDbContext context,
        IEmailService emailService,
        ILogger<CreateReservationCommandHandler> logger,
        IOptions<EmailSettings> emailSettings)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
        _emailSettings = emailSettings.Value;
    }

    public async Task<ApiResponse<ReservationDto>> Handle(CreateReservationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var data = command.ReservationData;

            // Validate date is not in the past
            if (data.ReservationDate.Date < DateTime.UtcNow.Date)
            {
                return ApiResponse<ReservationDto>.Failure("Cannot make reservations for past dates");
            }

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

            // Check for time slot conflicts
            var hasConflict = await _context.Reservations
                .AnyAsync(r =>
                    r.TableId == data.TableId &&
                    r.ReservationDate.Date == data.ReservationDate.Date &&
                    (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
                    ((r.StartTime < data.EndTime && r.EndTime > data.StartTime)), // Check overlap
                    cancellationToken);

            if (hasConflict)
            {
                return ApiResponse<ReservationDto>.Failure($"Table {table.TableNumber} is not available for the selected time slot");
            }

            // Create reservation
            var reservation = new Reservation
            {
                CustomerId = command.CustomerId,
                CustomerName = data.CustomerName,
                CustomerEmail = data.CustomerEmail,
                CustomerPhone = data.CustomerPhone,
                TableId = data.TableId,
                ReservationDate = data.ReservationDate,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
                NumberOfGuests = data.NumberOfGuests,
                Status = ReservationStatus.Pending,
                SpecialRequests = data.SpecialRequests,
                CreatedBy = command.CustomerId?.ToString() ?? "Guest"
            };

            _context.Reservations.Add(reservation);
            await _context.SaveChangesAsync(cancellationToken);

            // Send confirmation emails (customer and admin)
            try
            {
                // Send to customer
                await _emailService.SendReservationConfirmationEmailAsync(
                    reservation.CustomerEmail,
                    reservation.CustomerName,
                    table.TableNumber,
                    reservation.ReservationDate,
                    reservation.StartTime,
                    reservation.EndTime,
                    reservation.NumberOfGuests,
                    reservation.SpecialRequests);


                // Send to admin with action buttons
                var baseUrl = _emailSettings.BackendBaseUrl;
                var frontendUrl = _emailSettings.FrontendBaseUrl;

                await _emailService.SendEmailAsync(
                    _emailSettings.AdminEmail,
                    Common.Templates.EmailTemplates.ReservationAdminNotification.Subject,
                    Common.Templates.EmailTemplates.ReservationAdminNotification.GetHtmlBody(
                        reservation.Id,
                        reservation.CustomerName,
                        reservation.CustomerEmail,
                        reservation.CustomerPhone,
                        reservation.ReservationDate,
                        reservation.StartTime,
                        reservation.EndTime,
                        reservation.NumberOfGuests,
                        table.TableNumber,
                        baseUrl,
                        frontendUrl,
                        _emailSettings.AdminEmail,
                        reservation.SpecialRequests
                    ),
                    Common.Templates.EmailTemplates.ReservationAdminNotification.GetTextBody(
                        reservation.Id,
                        reservation.CustomerName,
                        reservation.CustomerEmail,
                        reservation.CustomerPhone,
                        reservation.ReservationDate,
                        reservation.StartTime,
                        reservation.EndTime,
                        reservation.NumberOfGuests,
                        table.TableNumber,
                        _emailSettings.AdminEmail,
                        reservation.SpecialRequests
                    ));


                _logger.LogInformation("Confirmation emails sent for reservation {ReservationId}", reservation.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send confirmation emails for reservation {ReservationId}, but reservation was created", reservation.Id);
                // Don't fail the reservation creation if email fails
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

            _logger.LogInformation("Created reservation {ReservationId} for table {TableNumber} on {Date}",
                reservation.Id, table.TableNumber, reservation.ReservationDate);

            return ApiResponse<ReservationDto>.SuccessWithData(reservationDto, "Reservation created successfully. You will receive a confirmation email once approved.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating reservation");
            return ApiResponse<ReservationDto>.Failure("Failed to create reservation");
        }
    }
}
