namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Strips provider request details before an exception reaches any logging sink.</summary>
internal static class AccountCheckoutDiagnostics
{
    internal static void Warn(ILogger logger, Exception failure, AccountCheckoutFailurePhase phase, Guid? attemptId = null)
    {
        var failureType = failure.GetType().Name;
        // No inner exception, original message, stack trace or Data: SDK errors can embed secrets.
        var diagnostic = new SanitizedCheckoutException(failureType);
        logger.LogWarning(diagnostic, "Account checkout {Phase} deferred for {AttemptId}: {FailureType}",
            phase, attemptId, failureType);
    }

    private sealed class SanitizedCheckoutException(string failureType)
        : Exception($"Account checkout failed with {failureType}.");
}

internal enum AccountCheckoutFailurePhase
{
    Sweep,
    ProviderRecovery,
    PostCommitNotification
}
