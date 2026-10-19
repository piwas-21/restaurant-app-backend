using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerCheckoutAcceptance(ICustomerCheckoutGuard guard, IBasketWriteCoordinator coordinator,
    ICustomerCheckoutDraft draft, ICustomerCheckoutIntentPolicy policy, ICustomerOrderOperationStore operations,
    IOrderFactory orders, IOrderCreationWriter writer, IBasketService baskets, IPreferredLanguageCapture languages,
    IOrderPostCreationService postCreation, ILogger<CustomerCheckoutAcceptance> logger) : ICustomerCheckoutAcceptance
{
    public Task<CustomerCheckoutPreparationDto> PrepareAsync(string sessionId, CustomerCheckoutIntentDto intent,
        CancellationToken cancellationToken)
    {
        var owner = guard.EnsureOwner(sessionId);
        return coordinator.RunAsync(sessionId, owner, async () =>
        {
            var context = await guard.ReviewAsync(sessionId, owner, intent.Review, cancellationToken);
            var prepared = await draft.PrepareAsync(context, intent.Review, cancellationToken);
            var quote = CustomerCheckoutProjection.Create(context, intent.Review, prepared.Order);
            await policy.EnsureAsync(intent, quote, cancellationToken);
            return new CustomerCheckoutPreparationDto(1, CustomerCheckoutIntentHash.Compute(intent), quote);
        }, cancellationToken);
    }

    public async Task<CustomerOrderResolutionDto> SubmitAsync(string sessionId, CustomerCheckoutSubmissionDto submission,
        CancellationToken cancellationToken)
    {
        var owner = guard.EnsureOwner(sessionId);
        if (!string.Equals(submission.PayloadHash, CustomerCheckoutIntentHash.Compute(submission.Intent), StringComparison.Ordinal))
            throw new ConflictException("The prepared order details changed. Review checkout again.");
        Order? accepted = null;
        var paysOnline = false;
        var resolution = await operations.ExecuteAsync(sessionId, submission.OperationId, submission.PayloadHash, async () =>
        {
            // Receipt replay precedes availability, repricing and number allocation. A withdrawal
            // after commit must never disguise an accepted order as a fresh submission.
            var intent = submission.Intent;
            var context = await guard.ReviewAsync(sessionId, owner, intent.Review, cancellationToken);
            var prepared = await draft.PrepareAsync(context, intent.Review, cancellationToken);
            var quote = CustomerCheckoutProjection.Create(context, intent.Review, prepared.Order);
            await policy.EnsureAsync(intent, quote, cancellationToken);
            var command = CustomerCheckoutCommandFactory.Create(intent, prepared, quote);
            var language = await languages.ForUserAsync(owner, cancellationToken);
            var orderDraft = await orders.CreateAsync(command, owner, language, cancellationToken);
            if (orderDraft.IsFailed) throw new BadRequestException(orderDraft.Error);
            var error = await writer.WriteNativeAsync(command, orderDraft, context.Currency, cancellationToken);
            if (error is not null) throw new BadRequestException(error);
            CustomerCheckoutMoneyGuard.Ensure(orderDraft.Order);
            var actual = CustomerCheckoutProjection.Create(context, intent.Review, orderDraft.Order);
            if (!string.Equals(actual.QuoteFingerprint, quote.QuoteFingerprint, StringComparison.Ordinal))
                throw new ConflictException("Checkout changed during acceptance. Review the current total.");
            await baskets.ClearBasketAsync(sessionId);
            accepted = orderDraft.Order;
            paysOnline = orderDraft.PaysOnline;
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
