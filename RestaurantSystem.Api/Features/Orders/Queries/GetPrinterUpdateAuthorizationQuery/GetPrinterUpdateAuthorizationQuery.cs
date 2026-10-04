using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetPrinterUpdateAuthorizationQuery;

public sealed record GetPrinterUpdateAuthorizationQuery(Guid JobId, int Revision, DevicePrintTarget Target)
    : IQuery<PrinterUpdateAuthorizationDto>;

public sealed class GetPrinterUpdateAuthorizationQueryHandler(ApplicationDbContext context)
    : IQueryHandler<GetPrinterUpdateAuthorizationQuery, PrinterUpdateAuthorizationDto>
{
    public async Task<PrinterUpdateAuthorizationDto> Handle(
        GetPrinterUpdateAuthorizationQuery query, CancellationToken cancellationToken)
    {
        var status = "Unavailable";
        if (query.JobId != Guid.Empty && Enum.IsDefined(query.Target)
            && query.Revision is PrinterUpdateRevisions.Original or PrinterUpdateRevisions.Withdrawal)
        {
            // soft-delete-bypass: hidden orders still revoke their cached correction jobs.
            var note = await context.OrderOperationalNotes.IgnoreQueryFilters().AsNoTracking()
                .Where(value => value.Id == query.JobId && value.Audience == OrderNoteAudience.Kitchen)
                .Select(value => new
                {
                    value.WithdrawnAt,
                    Target = value.KitchenTarget ?? DevicePrintTarget.General,
                    value.KitchenChangesJson,
                    value.Order.IsDeleted,
                    value.Order.Status
                }).SingleOrDefaultAsync(cancellationToken);
            if (note?.Target == query.Target)
            {
                if (note.WithdrawnAt.HasValue)
                    status = "Withdrawn";
                else if (query.Revision == PrinterUpdateRevisions.Original && !note.IsDeleted
                    && (note.KitchenChangesJson != null
                        || note.Status is OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.Ready))
                    status = "Authorized";
            }
        }

        return new PrinterUpdateAuthorizationDto(query.JobId, query.Revision, query.Target.ToString(), status);
    }
}
