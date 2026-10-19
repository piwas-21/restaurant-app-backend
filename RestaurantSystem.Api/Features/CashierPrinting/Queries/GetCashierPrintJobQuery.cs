using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Api.Features.CashierPrinting.Services;

namespace RestaurantSystem.Api.Features.CashierPrinting.Queries;

public sealed record GetCashierPrintJobQuery(Guid JobId) : IQuery<CashierPrintJobDto>;
public sealed class GetCashierPrintJobQueryHandler(ICashierPrintQueue queue)
    : IQueryHandler<GetCashierPrintJobQuery, CashierPrintJobDto>
{
    public Task<CashierPrintJobDto> Handle(GetCashierPrintJobQuery query, CancellationToken cancellationToken) =>
        queue.ReadAsync(query.JobId, cancellationToken);
}
