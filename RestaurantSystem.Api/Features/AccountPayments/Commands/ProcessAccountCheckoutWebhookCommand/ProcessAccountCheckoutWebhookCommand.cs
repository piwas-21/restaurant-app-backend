using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Commands.ProcessAccountCheckoutWebhookCommand;

public sealed record ProcessAccountCheckoutWebhookCommand(string Payload, string? Signature)
    : ICommand<AccountCheckoutWebhookDisposition>;

public sealed class ProcessAccountCheckoutWebhookCommandHandler(
    IAccountCheckoutWebhookService webhookService)
    : ICommandHandler<ProcessAccountCheckoutWebhookCommand, AccountCheckoutWebhookDisposition>
{
    public Task<AccountCheckoutWebhookDisposition> Handle(ProcessAccountCheckoutWebhookCommand command,
        CancellationToken cancellationToken) =>
        webhookService.HandleAsync(command.Payload, command.Signature, cancellationToken);
}
