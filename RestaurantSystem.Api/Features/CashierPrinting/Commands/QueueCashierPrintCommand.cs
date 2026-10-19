using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Api.Features.CashierPrinting.Services;

namespace RestaurantSystem.Api.Features.CashierPrinting.Commands;

public sealed record QueueCashierPrintCommand(QueueCashierPrintRequest Request) : ICommand<CashierPrintJobDto>;
public sealed class QueueCashierPrintCommandHandler(ICashierPrintQueue queue)
    : ICommandHandler<QueueCashierPrintCommand, CashierPrintJobDto>
{
    public Task<CashierPrintJobDto> Handle(QueueCashierPrintCommand command, CancellationToken cancellationToken) =>
        queue.QueueAsync(command.Request, false, cancellationToken);
}
