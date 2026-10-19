using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Api.Features.CashierPrinting.Services;

namespace RestaurantSystem.Api.Features.CashierPrinting.Commands;

public sealed record CompleteCashierPrintCommand(Guid JobId, string DeviceId, string Status) : ICommand<bool>;
public sealed class CompleteCashierPrintCommandHandler(ICashierPrintQueue queue)
    : ICommandHandler<CompleteCashierPrintCommand, bool>
{
    public async Task<bool> Handle(CompleteCashierPrintCommand command, CancellationToken cancellationToken)
    {
        await queue.CompleteAsync(command.JobId, command.DeviceId, command.Status, cancellationToken);
        return true;
    }
}
