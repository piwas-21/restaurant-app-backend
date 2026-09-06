using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Api.Features.Settings.FormFields;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.Api.Features.Reservations.Commands.UpdateReservationCommand;

public record UpdateReservationCommand(Guid ReservationId, UpdateReservationDto ReservationData)
    : ICommand<ApiResponse<ReservationDto>>;

public class UpdateReservationCommandHandler : ICommandHandler<UpdateReservationCommand, ApiResponse<ReservationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IEmailLanguageResolver _languages;
    private readonly IFormFieldRequirementService _formFieldRequirements;
    private readonly ILogger<UpdateReservationCommandHandler> _logger;

    public UpdateReservationCommandHandler(
        ApplicationDbContext context,
        IEmailService emailService,
        IEmailLanguageResolver languages,
        IFormFieldRequirementService formFieldRequirements,
        ILogger<UpdateReservationCommandHandler> logger)
    {
        _context = context;
        _emailService = emailService;
        _languages = languages;
        _formFieldRequirements = formFieldRequirements;
        _logger = logger;
    }

    public async Task<ApiResponse<ReservationDto>> Handle(UpdateReservationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var reservation = await _context.Reservations
                .Include(r => r.Table)
                .Include(r => r.CombinedTables).ThenInclude(c => c.Table)
                .FirstOrDefaultAsync(r => r.Id == command.ReservationId, cancellationToken);

            if (reservation == null)
            {
                return ApiResponse<ReservationDto>.Failure("Reservation not found");
            }

            var data = command.ReservationData;
            var previousStatus = reservation.Status;

            // Admin-configured requiredness, exactly as the create and guest-edit paths apply it.
            // This path used to assert it with a `[Required]` DataAnnotation instead, which ignored
            // the tenant's own setting in both directions: it refused a blank phone for a restaurant
            // that does not ask for one, and it is not what a restaurant that DOES ask for one is
            // configured through.
            await _formFieldRequirements.EnsureRequiredFieldsPresentAsync(
                FormFieldRegistry.FormKeys.Reservation,
                new Dictionary<string, string?>
                {
                    [FormFieldRegistry.ReservationFields.CustomerPhone] = data.CustomerPhone,
                    [FormFieldRegistry.ReservationFields.SpecialRequests] = data.SpecialRequests,
                },
                cancellationToken);

            // Validate table exists and is active
            var table = await _context.Tables
                .FirstOrDefaultAsync(t => t.Id == data.TableId && t.IsActive, cancellationToken);

            if (table == null)
            {
                return ApiResponse<ReservationDto>.Failure("Table not found or inactive");
            }

            // Capacity (#561): a combined booking fits when the SUM of the tables it occupies
            // fits. The PUT can re-seat the primary table, so the sum reads the POST-update set:
            // the new primary plus the untouched combined tables.
            var combinedTables = reservation.CombinedTables.Select(c => c.Table).ToList();
            var seatedCapacity = table.MaxGuests
                + combinedTables.Where(t => t.Id != table.Id).Sum(t => t.MaxGuests);
            if (seatedCapacity < data.NumberOfGuests)
            {
                return ApiResponse<ReservationDto>.Failure(
                    $"The selected tables can only accommodate {seatedCapacity} guests in total");
            }

            // Check for time slot conflicts (excluding current reservation) across EVERY table the
            // booking will occupy — its combined tables stay occupied by it too.
            var requestedTableIds = combinedTables.Select(t => t.Id)
                .Append(data.TableId)
                .Distinct()
                .ToList();
            var hasConflict = await _context.Reservations.AnyAsync(
                ReservationSlotOccupancy.ConflictsWithAnyOf(
                    requestedTableIds, data.ReservationDate, data.StartTime, data.EndTime, command.ReservationId),
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
                    // The guest's, frozen on the booking — this is a STAFF request (§6.10).
                    await _emailService.SendReservationApprovedEmailAsync(
                        _languages.ForGuest(reservation.PreferredLanguage),
                        reservation.CustomerEmail,
                        reservation.CustomerName,
                        new ReservationMailDetails(
                            reservation.ReservationDate,
                            reservation.StartTime,
                            reservation.EndTime,
                            reservation.NumberOfGuests,
                            table.TableNumber,
                            reservation.SpecialRequests),
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
                CustomerPhone = reservation.CustomerPhone ?? string.Empty,
                TableId = reservation.TableId,
                TableNumber = table.TableNumber,
                CombinedTableIds = reservation.CombinedTables.Select(c => c.TableId).ToList(),
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
