using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Api.Features.CashierPrinting.Services;

namespace RestaurantSystem.Api.Features.CashierPrinting.Commands;

public sealed record ClaimCashierPrintCommand(string DeviceId, bool AllowAutomatic = true) : ICommand<CashierPrintJobDto?>;
public sealed class ClaimCashierPrintCommandHandler(ICashierPrintQueue queue)
    : ICommandHandler<ClaimCashierPrintCommand, CashierPrintJobDto?>
{
    public Task<CashierPrintJobDto?> Handle(ClaimCashierPrintCommand command, CancellationToken cancellationToken) =>
        queue.ClaimAsync(command.DeviceId, cancellationToken, command.AllowAutomatic);
}
