using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Interfaces;

/// <summary>One tenant-scoped provider operation; no database transaction or guest identity.</summary>
public interface IAccountStripeCheckoutClient
{
    AccountStripeContext ReadContext();
    string ReadReturnBaseUrl();
    Task<AccountStripeSession> CreateAsync(AccountStripeCheckoutRequest request, CancellationToken cancellationToken);
    Task<AccountStripeSession?> GetAsync(string sessionId, CancellationToken cancellationToken);
    Task<AccountStripeIntent?> GetIntentAsync(string intentId, CancellationToken cancellationToken);
    Task<AccountStripeCharge?> GetChargeAsync(string chargeId, CancellationToken cancellationToken);
    Task ExpireAsync(string sessionId, CancellationToken cancellationToken);
}
