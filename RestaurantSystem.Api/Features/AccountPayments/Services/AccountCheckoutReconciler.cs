using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Each write phase has a fresh scoped unit of work; failed posting cannot leak tracked rows into recovery.</summary>
public sealed class AccountCheckoutReconciler(IServiceScopeFactory scopes,
    IAccountCheckoutEvidenceReader evidenceReader, IAccountCheckoutStatusReader statuses,
    ILogger<AccountCheckoutReconciler> logger) : IAccountCheckoutReconciler
{
    public async Task<AccountCheckoutStartDto> ReconcileAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var journal = await AcquireAsync(attemptId, cancellationToken);
        if (journal?.LeaseId is not Guid leaseId)
            return await statuses.ReadAsync(attemptId, null, cancellationToken);
        string? checkoutUrl = null;
        string? failureCode = null;
        try
        {
            var evidence = await evidenceReader.ReadAsync(journal, allowOriginalCreateRetry: true, cancellationToken);
            var verified = await RecordAsync(journal, evidence, cancellationToken);
            if (evidence.HasUnreversedCapture(verified))
                await PostAsync(verified, cancellationToken);
            if (evidence.Session.Status == "open" && evidence.Session.PaymentStatus == "unpaid")
                checkoutUrl = evidence.Session.Url;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The durable lease expires; the next canonical sweep resumes the same contribution.
            throw;
        }
        catch (Exception exception)
        {
            failureCode = "provider_recovery_deferred";
            // Provider messages can contain request detail. Log only the exception type and opaque attempt ID.
            logger.LogWarning("Account checkout recovery deferred for {AttemptId}: {FailureType}",
                attemptId, exception.GetType().Name);
        }
        await FinishAsync(attemptId, leaseId, failureCode, cancellationToken);
        return await statuses.ReadAsync(attemptId, failureCode is null ? checkoutUrl : null, cancellationToken);
    }

    private async Task<AccountCheckoutJournal?> AcquireAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountCheckoutLeaseStore>()
            .AcquireAsync(attemptId, cancellationToken);
    }

    private async Task<AccountCheckoutJournal> RecordAsync(AccountCheckoutJournal journal,
        AccountCheckoutCanonicalEvidence evidence, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountCheckoutEvidenceWriter>()
            .RecordAsync(journal, evidence, cancellationToken);
    }

    private async Task PostAsync(AccountCheckoutJournal verified, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAccountCheckoutCapturePoster>()
            .PostAsync(verified, cancellationToken);
    }

    private async Task FinishAsync(Guid attemptId, Guid leaseId, string? failureCode,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAccountCheckoutLeaseStore>()
            .FinishAsync(attemptId, leaseId, failureCode, cancellationToken);
    }
}
