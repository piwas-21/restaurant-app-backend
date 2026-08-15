using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Api.Features.Settings.FormFields;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateReservationCommand;

public record CreateReservationCommand(CreateReservationDto ReservationData, Guid? CustomerId = null)
    : ICommand<ApiResponse<ReservationDto>>;

public class CreateReservationCommandHandler : ICommandHandler<CreateReservationCommand, ApiResponse<ReservationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IReservationCreatedMailer _mailer;
    private readonly IFormFieldRequirementService _formFieldRequirements;
    private readonly IPreferredLanguageCapture _languages;
    private readonly ILogger<CreateReservationCommandHandler> _logger;

    public CreateReservationCommandHandler(
        ApplicationDbContext context,
        IReservationCreatedMailer mailer,
        IFormFieldRequirementService formFieldRequirements,
        IPreferredLanguageCapture languages,
        ILogger<CreateReservationCommandHandler> logger)
    {
        _context = context;
        _mailer = mailer;
        _formFieldRequirements = formFieldRequirements;
        _languages = languages;
        _logger = logger;
    }

    public async Task<ApiResponse<ReservationDto>> Handle(CreateReservationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var data = command.ReservationData;

            // Admin-configured requiredness (locked fields are covered by DataAnnotations).
            await _formFieldRequirements.EnsureRequiredFieldsPresentAsync(
                FormFieldRegistry.FormKeys.Reservation,
                new Dictionary<string, string?>
                {
                    [FormFieldRegistry.ReservationFields.CustomerPhone] = data.CustomerPhone,
                    [FormFieldRegistry.ReservationFields.SpecialRequests] = data.SpecialRequests,
                },
                cancellationToken);

            // Resolved before the conflict check, not between it and the insert: that check is an
            // unlocked read, so every await added after it widens the window in which two guests
            // can both be told they have the same table. Frozen on the row because the quick-action
            // links a reservation mail carries run with no request language at all — this column is
            // the only thing that can tell the approve/reject mail what to write in.
            var language = await _languages.ForUserAsync(command.CustomerId, cancellationToken);

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
                PreferredLanguage = language,
                CreatedBy = command.CustomerId?.ToString() ?? "Guest"
            };

            _context.Reservations.Add(reservation);
            await _context.SaveChangesAsync(cancellationToken);

            await _mailer.SendAsync(reservation, table.TableNumber, cancellationToken);

            var reservationDto = ReservationDtoMapper.ToDto(reservation, table.TableNumber);

            _logger.LogInformation("Created reservation {ReservationId} for table {TableNumber} on {Date}",
                reservation.Id, table.TableNumber, reservation.ReservationDate);

            return ApiResponse<ReservationDto>.SuccessWithData(reservationDto, "Reservation created successfully. You will receive a confirmation email once approved.");
        }
        // BadRequestException carries the config-driven required-field message —
        // let the exception middleware map it to a 400 instead of swallowing it here.
        catch (Exception ex) when (ex is not Common.Exceptions.BadRequestException)
        {
            _logger.LogError(ex, "Error creating reservation");
            return ApiResponse<ReservationDto>.Failure("Failed to create reservation");
        }
    }
}
