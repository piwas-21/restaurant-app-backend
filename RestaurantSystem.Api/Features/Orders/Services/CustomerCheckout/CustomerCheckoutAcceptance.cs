using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

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
        Order? accepted = null;
        var paysOnline = false;
        var resolution = await operations.ExecuteAsync(sessionId, submission.OperationId, submission.PayloadHash, async () =>
        {
            // Receipt replay precedes availability, repricing and number allocation. A withdrawal
            // after commit must never disguise an accepted order as a fresh submission.
            var draft = await creator.CreateUnderLockAsync(sessionId, submission.Intent, cancellationToken);
            accepted = draft.Order;
            paysOnline = draft.PaysOnline;
            return accepted.Id;
        }, cancellationToken);
        if (accepted is not null)
        {
            try
            {
                await postCreation.CompleteAsync(accepted, paysOnline, cancellationToken);
            }
            catch (Exception)
            {
                // Acceptance already committed. Delivery failures cannot turn the response into
                // a new purchase opportunity; private contact values are deliberately not logged.
                logger.LogWarning("Post-commit notification failed for native order {OrderId}", accepted.Id);
            }
        }
        return Resolution(submission.OperationId, resolution);
    }

    public async Task<CustomerOrderResolutionDto> RecoverAsync(string sessionId, CustomerOrderRecoveryDto recovery,
        CancellationToken cancellationToken) => Resolution(recovery.OperationId,
        await operations.RecoverAsync(sessionId, recovery.OperationId, recovery.PayloadHash, cancellationToken));

    public async Task<CustomerOrderResolutionDto> CancelAsync(string sessionId, CustomerOrderRecoveryDto recovery,
        CancellationToken cancellationToken) => Resolution(recovery.OperationId,
        await operations.CancelAsync(sessionId, recovery.OperationId, recovery.PayloadHash, cancellationToken));

    private static CustomerOrderResolutionDto Resolution(Guid operationId, CustomerOrderOperationResolution resolution) =>
        new(1, operationId, resolution.Outcome, resolution.OrderId);
}
