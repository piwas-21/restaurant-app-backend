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
    private readonly ITenantClock _clock;
    private readonly ILogger<CreateReservationCommandHandler> _logger;

    public CreateReservationCommandHandler(
        ApplicationDbContext context,
        IReservationCreatedMailer mailer,
        IFormFieldRequirementService formFieldRequirements,
        IPreferredLanguageCapture languages,
        ITenantClock clock,
        ILogger<CreateReservationCommandHandler> logger)
    {
        _context = context;
        _mailer = mailer;
        _formFieldRequirements = formFieldRequirements;
        _languages = languages;
        _clock = clock;
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

            // "Today" is the restaurant's day, not UTC's. Read against UtcNow this refused a
            // booking for TONIGHT for any tenant west of UTC after 19:00 local — a zone
            // TENANT_TIMEZONE now makes settable (#369). Both sides are calendar days here, so
            // their DateTimeKind is deliberately not part of the comparison.
            var tenantToday = _clock.Now.Date;

            if (data.ReservationDate.Date < tenantToday)
            {
                return ApiResponse<ReservationDto>.Failure("Cannot make reservations for past dates");
            }

            // Validate tables: the primary AND every combined table must exist and be active.
            // A combined booking is ONE reservation over N tables (#561).
            var combinedTableIds = data.CombinedTableIds ?? new List<Guid>();
            var requestedTableIds = combinedTableIds.Append(data.TableId).Distinct().ToList();

            var tables = await _context.Tables
                .Where(t => requestedTableIds.Contains(t.Id) && t.IsActive)
                .ToListAsync(cancellationToken);

            if (tables.Count != requestedTableIds.Count)
            {
                return ApiResponse<ReservationDto>.Failure("Table not found or inactive");
            }

            var table = tables.Single(t => t.Id == data.TableId);

            // Capacity: the party fits when the SUM of the set's capacities fits — the point of
            // combining is that INDIVIDUAL tables may each be smaller than the party.
            var seatedCapacity = tables.Sum(t => t.MaxGuests);
            if (seatedCapacity < data.NumberOfGuests)
            {
                return ApiResponse<ReservationDto>.Failure(
                    $"The selected tables can only accommodate {seatedCapacity} guests in total");
            }

            // Check for time slot conflicts across EVERY table the booking would occupy — a
            // combined reservation elsewhere blocks each of its tables here too.
            var hasConflict = await _context.Reservations.AnyAsync(
                ReservationSlotOccupancy.ConflictsWithAnyOf(
                    requestedTableIds, data.ReservationDate, data.StartTime, data.EndTime),
                cancellationToken);

            if (hasConflict)
            {
                return ApiResponse<ReservationDto>.Failure("One or more of the selected tables is not available for the selected time slot");
            }

            // Create reservation — with one child row per combined table, so every occupancy
            // read of these tables sees this booking (#561).
            var createdBy = command.CustomerId?.ToString() ?? "Guest";
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
                CreatedBy = createdBy,
                CombinedTables = combinedTableIds
                    .Where(id => id != data.TableId)
                    .Select(id => new ReservationTable { TableId = id, CreatedBy = createdBy })
                    .ToList()
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
