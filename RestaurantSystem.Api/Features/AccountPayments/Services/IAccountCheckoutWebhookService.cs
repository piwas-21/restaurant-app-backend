namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountCheckoutWebhookService
{
    Task<AccountCheckoutWebhookDisposition> HandleAsync(string payload, string? signature,
        CancellationToken cancellationToken);
}
