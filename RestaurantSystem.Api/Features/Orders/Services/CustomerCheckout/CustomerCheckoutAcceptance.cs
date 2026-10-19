using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerCheckoutAcceptance(ICustomerCheckoutGuard guard, ICustomerCheckoutPreparation preparation,
    ICustomerOrderOperationStore operations, ICustomerCheckoutOrderCreator creator,
    IOrderPostCreationService postCreation, ILogger<CustomerCheckoutAcceptance> logger) : ICustomerCheckoutAcceptance
{
    public Task<CustomerCheckoutPreparationDto> PrepareAsync(string sessionId, CustomerCheckoutIntentDto intent,
        CancellationToken cancellationToken) => preparation.PrepareAsync(sessionId, intent, cancellationToken);

    public async Task<CustomerOrderResolutionDto> SubmitAsync(string sessionId, CustomerCheckoutSubmissionDto submission,
        CancellationToken cancellationToken)
    {
        guard.EnsureOwner(sessionId);
        if (!string.Equals(submission.PayloadHash, CustomerCheckoutIntentHash.Compute(submission.Intent), StringComparison.Ordinal))
            throw new ConflictException("The prepared order details changed. Review checkout again.");
        var firstAcceptance = new List<CustomerCheckoutAccepted>(1);
        var resolution = await operations.ExecuteAsync(sessionId, submission.OperationId, submission.PayloadHash, async () =>
        {
            // Receipt replay precedes availability, repricing and number allocation. A withdrawal
            // after commit must never disguise an accepted order as a fresh submission.
            var draft = await creator.CreateUnderLockAsync(sessionId, submission.Intent, cancellationToken);
            firstAcceptance.Add(draft);
            return draft.Order.Id;
        }, cancellationToken);
        foreach (var accepted in firstAcceptance)
        {
            if (!await TryNotifyAsync(accepted, cancellationToken))
                logger.LogWarning("Post-commit notification failed for native order {OrderId}", accepted.Order.Id);
        }
        return Resolution(submission.OperationId, resolution);
    }

    public async Task<CustomerOrderResolutionDto> RecoverAsync(string sessionId, CustomerOrderRecoveryDto recovery,
        CancellationToken cancellationToken) => Resolution(recovery.OperationId,
        await operations.RecoverAsync(sessionId, recovery.OperationId, recovery.PayloadHash, cancellationToken));

    public async Task<CustomerOrderResolutionDto> CancelAsync(string sessionId, CustomerOrderRecoveryDto recovery,
        CancellationToken cancellationToken) => Resolution(recovery.OperationId,
        await operations.CancelAsync(sessionId, recovery.OperationId, recovery.PayloadHash, cancellationToken));

    private async Task<bool> TryNotifyAsync(CustomerCheckoutAccepted accepted, CancellationToken cancellationToken)
    {
        try
        {
            await postCreation.CompleteAsync(accepted.Order, accepted.PaysOnline, cancellationToken);
            return true;
        }
        catch (Exception)
        {
            // Acceptance already committed. Do not propagate or log an exception that may contain
            // contact data; the caller records only the failed-delivery fact and order identity.
            return false;
        }
    }

    private static CustomerOrderResolutionDto Resolution(Guid operationId, CustomerOrderOperationResolution resolution) =>
        new(1, operationId, resolution.Outcome, resolution.OrderId);
}
