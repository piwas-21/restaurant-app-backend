using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Settings.FormFields;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Commands.UpdateMyReservationCommand;

/// <summary>The caller is NOT part of this record on purpose: it is read from the bearer token
/// inside the handler, so no route can hand it somebody else's id.</summary>
public record UpdateMyReservationCommand(Guid ReservationId, UpdateMyReservationDto ReservationData)
    : ICommand<ApiResponse<ReservationDto>>;

public class UpdateMyReservationCommandHandler
    : ICommandHandler<UpdateMyReservationCommand, ApiResponse<ReservationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IFormFieldRequirementService _formFieldRequirements;
    private readonly ITenantClock _clock;
    private readonly ILogger<UpdateMyReservationCommandHandler> _logger;

    public UpdateMyReservationCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IFormFieldRequirementService formFieldRequirements,
        ITenantClock clock,
        ILogger<UpdateMyReservationCommandHandler> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _formFieldRequirements = formFieldRequirements;
        _clock = clock;
        _logger = logger;
    }

    // No try/catch: NotFoundException / BadRequestException carry this endpoint's whole error
    // contract and must reach ExceptionHandlingMiddleware, which maps them to 404/400 with a stable
    // ErrorCode. Swallowing them, as the admin path does, flattens each refusal into an untyped 400.
    public async Task<ApiResponse<ReservationDto>> Handle(
        UpdateMyReservationCommand command, CancellationToken cancellationToken)
    {
        var reservation = await LoadOwnReservationAsync(command.ReservationId, cancellationToken);
        var data = command.ReservationData;

        // Admin-configured requiredness, exactly as the create path applies it: a restaurant that
        // demands a phone number must not lose it because the guest edited the booking.
        await _formFieldRequirements.EnsureRequiredFieldsPresentAsync(
            FormFieldRegistry.FormKeys.Reservation,
            new Dictionary<string, string?>
            {
                [FormFieldRegistry.ReservationFields.CustomerPhone] = data.CustomerPhone,
                [FormFieldRegistry.ReservationFields.SpecialRequests] = data.SpecialRequests,
            },
            cancellationToken);

        // The calendar day at UTC midnight — what the clients send the create path, and the only
        // Kind Npgsql accepts against a timestamptz column. Normalised ONCE so the conflict query
        // and the write cannot disagree about which day this is.
        var bookedDay = DateTime.SpecifyKind(data.ReservationDate.Date, DateTimeKind.Utc);

        // The restaurant's day, never UTC's (#369). Both sides are CALENDAR DAYS: the booked day
        // is not an instant and is never run through the clock (#363).
        var tenantToday = _clock.Now.Date;

        EnsureEditable(reservation, tenantToday);

        if (bookedDay.Date < tenantToday)
        {
            throw new BadRequestException(
                "Cannot make reservations for past dates", ErrorCodes.ReservationDateInPast);
        }

        await EnsureTableStillFitsAsync(reservation, data, bookedDay, cancellationToken);

        ApplyGuestEditableFields(reservation, data, bookedDay);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Customer {CustomerId} updated their reservation {ReservationId}",
            reservation.CustomerId, reservation.Id);

        return ApiResponse<ReservationDto>.SuccessWithData(
            ReservationDtoMapper.ToDto(reservation, reservation.Table.TableNumber),
            "Reservation updated successfully");
    }

    /// <summary>The reservation, only if the caller owns it. Missing, someone else's, and a guest
    /// booking (<c>CustomerId == null</c>) all answer with the SAME 404: a distinct 403 would confirm
    /// the id exists and make this route an oracle for enumerating real reservations.</summary>
    private async Task<Reservation> LoadOwnReservationAsync(Guid id, CancellationToken cancellationToken)
    {
        var reservation = await _context.Reservations
            .Include(r => r.Table)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        var userId = _currentUser.UserId;

        // Staff get no override here by design — they have the admin PUT, which is the route that
        // may set status, table and notes. This one is "my own booking" and nothing else.
        if (reservation == null || !userId.HasValue || reservation.CustomerId != userId.Value)
        {
            _logger.LogWarning(
                "User {UserId} may not edit reservation {ReservationId}; responding as not-found",
                userId, id);
            throw new NotFoundException("Reservation not found", ErrorCodes.ReservationNotFound);
        }

        return reservation;
    }

    /// <summary>Only a live, still-future booking is guest-editable: the cancel path's
    /// terminal-status rule plus the create path's day comparison — not a second time model.</summary>
    private static void EnsureEditable(Reservation reservation, DateTime tenantToday)
    {
        if (reservation.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
        {
            throw new BadRequestException(
                $"A {reservation.Status.ToString().ToLowerInvariant()} reservation can no longer be changed",
                ErrorCodes.ReservationNotEditable);
        }

        if (reservation.ReservationDate.Date < tenantToday)
        {
            throw new BadRequestException(
                "A past reservation can no longer be changed", ErrorCodes.ReservationNotEditable);
        }
    }

    /// <summary>The create path's capacity and overlap checks, against the table the booking already
    /// sits on. No re-assignment: the guest DTO carries no <c>TableId</c>, and silently moving a party
    /// to another table changes what the restaurant agreed to.</summary>
    private async Task EnsureTableStillFitsAsync(
        Reservation reservation,
        UpdateMyReservationDto data,
        DateTime bookedDay,
        CancellationToken cancellationToken)
    {
        var table = reservation.Table;

        if (table.MaxGuests < data.NumberOfGuests)
        {
            throw new BadRequestException(
                $"Table {table.TableNumber} can only accommodate {table.MaxGuests} guests",
                ErrorCodes.ReservationTableCapacityExceeded);
        }

        var hasConflict = await _context.Reservations
            .AnyAsync(r =>
                r.Id != reservation.Id &&
                r.TableId == reservation.TableId &&
                r.ReservationDate.Date == bookedDay.Date &&
                (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
                r.StartTime < data.EndTime && r.EndTime > data.StartTime,
                cancellationToken);

        if (hasConflict)
        {
            throw new BadRequestException(
                $"Table {table.TableNumber} is not available for the selected time slot",
                ErrorCodes.ReservationSlotUnavailable);
        }
    }

    /// <summary>Writes the guest-editable fields only. A Confirmed booking whose SHAPE changed (day,
    /// hours or party size) drops back to Pending — the restaurant approved those numbers, not the new
    /// ones. A contact-detail-only edit keeps the confirmation.</summary>
    private static void ApplyGuestEditableFields(
        Reservation reservation, UpdateMyReservationDto data, DateTime bookedDay)
    {
        var shapeChanged =
            reservation.ReservationDate.Date != bookedDay.Date ||
            reservation.StartTime != data.StartTime ||
            reservation.EndTime != data.EndTime ||
            reservation.NumberOfGuests != data.NumberOfGuests;

        reservation.CustomerName = data.CustomerName;
        reservation.CustomerEmail = data.CustomerEmail;
        reservation.CustomerPhone = data.CustomerPhone;
        reservation.ReservationDate = bookedDay;
        reservation.StartTime = data.StartTime;
        reservation.EndTime = data.EndTime;
        reservation.NumberOfGuests = data.NumberOfGuests;
        reservation.SpecialRequests = data.SpecialRequests;

        if (shapeChanged && reservation.Status == ReservationStatus.Confirmed)
        {
            reservation.Status = ReservationStatus.Pending;
        }
    }
}
