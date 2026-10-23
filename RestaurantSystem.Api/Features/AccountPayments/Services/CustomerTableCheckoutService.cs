using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class CustomerTableCheckoutService(ICustomerTableCheckoutStore store,
    IAccountCheckoutReconciler reconciler, ICustomerTableCheckoutProjection projection,
    IOptions<CustomerHostedCheckoutSettings> hosted) : ICustomerTableCheckoutService
{
    public async Task<CustomerTableCheckoutDto> RunAsync(string sessionId, CustomerTablePaymentProofDto proof,
        string? participantToken, CustomerTableCheckoutAction action, CancellationToken cancellationToken)
    {
        var original = await store.RunAsync(sessionId, proof, participantToken, action, cancellationToken);
        string? url = null;
        if (original.HasJournal && original.AttemptId is Guid attemptId)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(hosted.Value.ReconciliationBudgetSeconds,
                1, CustomerHostedCheckoutSettings.MaximumReconciliationBudgetSeconds)));
            try { url = (await reconciler.ReconcileAsync(attemptId, budget.Token)).CheckoutUrl; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { /* The original journal/lease survives; a bounded request cannot release or replace it. */ }
        }
        return await projection.ReadAsync(sessionId, proof, original, participantToken, url, cancellationToken);
    }
}
