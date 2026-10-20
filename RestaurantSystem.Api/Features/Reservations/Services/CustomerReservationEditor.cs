using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using LegacySlots = RestaurantSystem.Api.Features.Reservations.Queries.GetAvailableTimeSlotsQuery.GetAvailableTimeSlotsQuery;
using RestaurantSystem.Api.Features.Settings.FormFields;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationEditor(ApplicationDbContext context, ICustomerReservationEditReadAccess reads,
    CustomMediator mediator, IFormFieldRequirementService requirements, ITenantClock clock) : ICustomerReservationEditor
{
    public async Task<CustomerReservationEditDraft> ReviewAsync(CustomerReservationEditDto request, Reservation row,
        CustomerReservationOperationOwner owner, CancellationToken cancellationToken)
    {
        if (!(await new CustomerReservationEditDtoValidator().ValidateAsync(request, cancellationToken)).IsValid)
            throw new BadRequestException("Review the reservation details before changing this booking.");
        var original = await reads.ReadAsync(request.ReservationId, cancellationToken);
        if (original.Owner != owner.CustomerId || original.Revision != row.Revision) throw Changed();
        if (original.Context.TenantId != request.TenantId || original.Context.TimeZone != request.TimeZone)
            throw new NotFoundException("Customer reservation editor unavailable. Refresh this restaurant.");
        // Lock the complete existing arrangement in a stable order; table configuration cannot change before commit.
        var assigned = original.AssignedTables.Order().ToArray();
        var locked = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM "Tables" WHERE id = ANY({assigned}) ORDER BY id FOR SHARE
            """).ToArrayAsync(cancellationToken);
        if (locked.Length != assigned.Length) throw Changed();
        var data = new UpdateMyReservationDto
        {
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            CustomerPhone = request.CustomerPhone,
            SpecialRequests = request.SpecialRequests,
            ReservationDate = request.Date!.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            StartTime = request.StartTime!.Value,
            EndTime = request.EndTime!.Value,
            NumberOfGuests = request.NumberOfGuests!.Value
        };
        // row is detached. Preparing may compute the proposal but never writes it.
        var edit = GuestReservationEdit.Apply(row, data, data.ReservationDate);
        await RequireFieldsAsync(request, cancellationToken);
        await RequireArrangementAsync(request, original.AssignedTables, edit.ShapeChanged, cancellationToken);
        await reads.RevalidateAsync(original, cancellationToken);
        CustomerReservationEditability.RequireFuture(ReservationStatus.Pending, request.Date!.Value, request.StartTime!.Value, clock.Now);
        var tableNumber = await context.Tables.AsNoTracking().Where(table => table.Id == row.TableId)
            .Select(table => table.TableNumber).SingleAsync(cancellationToken);
        return new(original, row, edit, tableNumber);
    }
    public async Task RevalidateAsync(CustomerReservationEditDto request, CustomerReservationEditReadSnapshot applied,
        ReservationEdit originalEdit, CancellationToken cancellationToken)
    {
        await reads.RevalidateAsync(applied, cancellationToken);
        await RequireFieldsAsync(request, cancellationToken);
        CustomerReservationEditability.RequireFuture(originalEdit.WasConfirmed ? ReservationStatus.Confirmed : ReservationStatus.Pending,
            DateOnly.FromDateTime(originalEdit.PreviousDate), originalEdit.PreviousStartTime, clock.Now);
        await RequireArrangementAsync(request, applied.AssignedTables, originalEdit.ShapeChanged, cancellationToken);
        await reads.RevalidateAsync(applied, cancellationToken);
        CustomerReservationEditability.RequireFuture(ReservationStatus.Pending, request.Date!.Value, request.StartTime!.Value, clock.Now);
    }
    private Task RequireFieldsAsync(CustomerReservationEditDto request, CancellationToken cancellationToken) =>
        requirements.EnsureRequiredFieldsPresentAsync(FormFieldRegistry.FormKeys.Reservation,
            new Dictionary<string, string?>
            {
                [FormFieldRegistry.ReservationFields.CustomerPhone] = request.CustomerPhone,
                [FormFieldRegistry.ReservationFields.SpecialRequests] = request.SpecialRequests
            }, cancellationToken);
    private async Task RequireArrangementAsync(CustomerReservationEditDto request, IReadOnlyList<Guid> assigned,
        bool shapeChanged, CancellationToken cancellationToken)
    {
        if (request.Date is not { } date || request.NumberOfGuests is not { } guests) throw Changed();
        var marker = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        if (!shapeChanged)
        {
            // A contact correction preserves a previously agreed sitting, including off-grid or changed hours.
            var tables = await context.Tables.AsNoTracking().Where(table => assigned.Contains(table.Id))
                .Select(table => new TableDto { Id = table.Id, MaxGuests = table.MaxGuests, IsActive = table.IsActive })
                .ToArrayAsync(cancellationToken);
            if (!CustomerReservationAssignedSlots.Fits(tables, assigned, guests)
                || await context.Reservations.AnyAsync(ReservationSlotOccupancy.ConflictsWithAnyOf(assigned.ToArray(), marker,
                    request.StartTime!.Value, request.EndTime!.Value, request.ReservationId), cancellationToken)) throw Changed();
            return;
        }
        var available = await mediator.SendQuery(new LegacySlots(marker, guests)
        { ExcludedReservationId = request.ReservationId }, cancellationToken);
        if (!available.Success || available.Data is null
            || !available.Data.TimeSlots.Any(slot => slot.StartTime == request.StartTime && slot.EndTime == request.EndTime
                && CustomerReservationAssignedSlots.Fits(slot.AvailableTables, assigned, guests))) throw Changed();
    }
    private static ConflictException Changed() => new("This booking or its time changed. Review current details and available times.");
}
