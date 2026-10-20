using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Reservations.Dtos;
using LegacySlots = RestaurantSystem.Api.Features.Reservations.Queries.GetAvailableTimeSlotsQuery.GetAvailableTimeSlotsQuery;

namespace RestaurantSystem.Api.Features.Reservations.Services;

public sealed class CustomerReservationCreator(CustomMediator mediator, IReservationDraftWriter writer,
    IPreferredLanguageCapture languages, ICustomerReservationOperationGuard guard) : ICustomerReservationCreator
{
    public async Task<ReservationCreationDraft> StageAsync(CustomerReservationCreateDto request,
        CustomerReservationOperationOwner owner, CancellationToken cancellationToken)
    {
        await guard.RequireAcceptingAsync(request, owner, cancellationToken);
        if (request.Date is not { } date || request.StartTime is not { } start || request.EndTime is not { } end
            || request.NumberOfGuests is not { } guests || guests is < 1 or > 20)
            throw new BadRequestException("Choose a reservation date, time and party size.");
        var marker = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        // Recompute the shared split-shift/combined occupancy engine INSIDE schedule authority.
        var available = await mediator.SendQuery(new LegacySlots(marker, guests), cancellationToken);
        var slot = available.Success ? available.Data?.TimeSlots.SingleOrDefault(row => row.StartTime == start
            && row.EndTime == end && start >= TimeSpan.Zero && end <= TimeSpan.FromDays(1) && end > start) : null;
        if (slot is null) throw Changed();
        var ids = ChooseTables(slot.AvailableTables, guests);
        var data = new CreateReservationDto
        {
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            CustomerPhone = request.CustomerPhone,
            SpecialRequests = request.SpecialRequests,
            ReservationDate = marker,
            StartTime = start,
            EndTime = end,
            NumberOfGuests = guests,
            TableId = ids[0],
            CombinedTableIds = ids.Skip(1).ToList()
        };
        var language = await languages.ForUserAsync(owner.CustomerId, cancellationToken);
        var draft = await writer.CreateUnderLockAsync(data, owner.CustomerId, language, cancellationToken);
        if (draft.Error is not null) throw new ConflictException(draft.Error);
        await guard.RequireAcceptingAsync(request, owner, cancellationToken);
        return draft;
    }

    private static List<Guid> ChooseTables(IEnumerable<TableDto> availableTables, int guests)
    {
        var tables = availableTables.Where(row => row.Id != Guid.Empty && row.MaxGuests > 0)
            .DistinctBy(row => row.Id).OrderBy(row => row.MaxGuests).ThenBy(row => row.Id).ToArray();
        var single = tables.FirstOrDefault(row => row.MaxGuests >= guests);
        var selected = single is null ? tables.OrderByDescending(row => row.MaxGuests).ThenBy(row => row.Id)
            .Take(guests).ToList() : new List<TableDto> { single };
        // Stop once enough capacity is selected; private table identities never leave this service.
        var ids = new List<Guid>();
        long capacity = 0;
        foreach (var table in selected)
        {
            ids.Add(table.Id);
            capacity += table.MaxGuests;
            if (capacity >= guests) break;
        }
        if (capacity < guests) throw Changed();
        return ids;
    }

    private static ConflictException Changed() => new("This reservation time is no longer available. Check current times.");
}
