using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using RestaurantSystem.Api.Features.Settings.FormFields;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class ReservationDraftWriter(ApplicationDbContext context,
    IFormFieldRequirementService formFieldRequirements, ITenantClock clock) : IReservationDraftWriter
{
    private readonly ApplicationDbContext _context = context;
    private readonly IFormFieldRequirementService _formFieldRequirements = formFieldRequirements;
    private readonly ITenantClock _clock = clock;

    public async Task<ReservationCreationDraft> CreateUnderLockAsync(CreateReservationDto data, Guid? customerId,
        string? language, CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Reservation drafts require the caller's schedule transaction.");
        // Admin-configured requiredness (locked fields are covered by DataAnnotations).
        await _formFieldRequirements.EnsureRequiredFieldsPresentAsync(
            FormFieldRegistry.FormKeys.Reservation,
            new Dictionary<string, string?>
            {
                [FormFieldRegistry.ReservationFields.CustomerPhone] = data.CustomerPhone,
                [FormFieldRegistry.ReservationFields.SpecialRequests] = data.SpecialRequests,
            },
            cancellationToken);

        // "Today" is the restaurant's day, not UTC's. Read against UtcNow this refused a
        // booking for TONIGHT for any tenant west of UTC after 19:00 local — a zone
        // TENANT_TIMEZONE now makes settable (#369). Both sides are calendar days here, so
        // their DateTimeKind is deliberately not part of the comparison.
        var tenantToday = _clock.Now.Date;

        if (data.ReservationDate.Date < tenantToday)
        {
            return ReservationCreationDraft.Refused("Cannot make reservations for past dates");
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
            return ReservationCreationDraft.Refused("Table not found or inactive");
        }

        var table = tables.Single(t => t.Id == data.TableId);

        // Capacity: the party fits when the SUM of the set's capacities fits — the point of
        // combining is that INDIVIDUAL tables may each be smaller than the party.
        var seatedCapacity = tables.Sum(t => t.MaxGuests);
        if (seatedCapacity < data.NumberOfGuests)
        {
            return ReservationCreationDraft.Refused(
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
            return ReservationCreationDraft.Refused("One or more of the selected tables is not available for the selected time slot");
        }

        // Create reservation — with one child row per combined table, so every occupancy
        // read of these tables sees this booking (#561).
        var createdBy = customerId?.ToString() ?? "Guest";
        var reservation = new Reservation
        {
            // The native receipt and booking need one stable identity BEFORE their shared save.
            Id = Guid.NewGuid(),
            CustomerId = customerId,
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
        return new(reservation, table.TableNumber, null);
    }
}
