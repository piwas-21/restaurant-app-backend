using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

internal sealed class MutableAmendmentFeatures : RestaurantSystem.Api.Common.TenantFeatures.ITenantFeatures
{
    public bool OrderAmendmentsV1 { get; set; } = true;
    public bool ServerWorkspaceV2 => false;
    public bool TableAccountV1 => false;
    public bool TableGuestVisitsV1 => false;
    public bool TableVisitReadinessV1 => false;
    public bool TableAccountPaymentsV1 => false;
    public bool TableGuestAccountPaymentsV1 => false;
    public bool EnforceSauceMinimum => false;
    public bool OptionSetMaterializationEnabled => false;
}

internal sealed class FakeAmendmentRefundState
{
    internal const string AccountId = "acct_amendment_test";
    internal const string SessionId = "cs_amendment_test";
    internal const string IntentId = "pi_amendment_test";
    internal const string ChargeId = "ch_amendment_test";

    private readonly Lock _lock = new();
    private readonly List<AmendmentRefundEvidence> _refunds = [];
    private int listCalls;

    internal int CreateCalls { get; set; }
    internal bool LoseFirstCreateResponse { get; set; } = true;
    internal long? CanonicalRefundedMinorOverride { get; set; }
    internal Queue<string> CreateStatuses { get; } = new();
    internal string DefaultCreateStatus { get; set; } = "succeeded";
    internal bool ProviderIoObservedDatabaseTransaction { get; set; }
    internal bool ProviderRequestWasDurableBeforeCreate { get; set; }
    internal FakeAmendmentRefundResponseGate? CreateResponseGate { get; set; }
    internal int ListCalls => Volatile.Read(ref listCalls);
    internal Dictionary<int, FakeAmendmentRefundListResponseGate> ListResponseGates { get; } = [];
    internal AmendmentRefundRequest? LastRequest { get; set; }
    internal List<AmendmentRefundEvidence> CreatedEvidence { get; } = [];

    internal IReadOnlyList<AmendmentRefundEvidence> ReadRefunds()
    {
        lock (_lock)
            return _refunds.ToArray();
    }

    internal void AddRefund(AmendmentRefundEvidence evidence)
    {
        lock (_lock)
        {
            var exists = _refunds.Any(value => value.Metadata.TryGetValue(
                StripeOrderAmendmentRefundProvider.AttemptKey, out var existingAttempt)
                && evidence.Metadata.TryGetValue(StripeOrderAmendmentRefundProvider.AttemptKey,
                    out var newAttempt) && existingAttempt == newAttempt);
            if (!exists)
            {
                _refunds.Add(evidence);
                CreatedEvidence.Add(evidence);
            }
        }
    }

    internal void SetRefundStatus(string refundId, string status)
    {
        lock (_lock)
        {
            var index = _refunds.FindIndex(value => value.RefundId == refundId);
            if (index < 0) throw new InvalidOperationException("The fake refund identity was not found.");
            _refunds[index] = _refunds[index] with { Status = status };
        }
    }

    internal string NextCreateStatus() => CreateStatuses.Count > 0
        ? CreateStatuses.Dequeue() : DefaultCreateStatus;

    internal int NextListCall() => Interlocked.Increment(ref listCalls);
}

internal sealed class FakeAmendmentRefundResponseGate
{
    private readonly TaskCompletionSource<AmendmentRefundEvidence> responsePrepared =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource releaseResponse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task<AmendmentRefundEvidence> ResponsePrepared => responsePrepared.Task;

    internal void Prepare(AmendmentRefundEvidence response) => responsePrepared.TrySetResult(response);

    internal Task WaitForReleaseAsync(CancellationToken cancellationToken) =>
        releaseResponse.Task.WaitAsync(cancellationToken);

    internal void Release() => releaseResponse.TrySetResult();
}

internal sealed class FakeAmendmentRefundListResponseGate
{
    private readonly TaskCompletionSource<IReadOnlyList<AmendmentRefundEvidence>> responsePrepared =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource releaseResponse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task<IReadOnlyList<AmendmentRefundEvidence>> ResponsePrepared => responsePrepared.Task;

    internal void Prepare(IReadOnlyList<AmendmentRefundEvidence> response) =>
        responsePrepared.TrySetResult(response);

    internal Task WaitForReleaseAsync(CancellationToken cancellationToken) =>
        releaseResponse.Task.WaitAsync(cancellationToken);

    internal void Release() => releaseResponse.TrySetResult();
}

internal sealed class FakeAmendmentRefundProvider(
    FakeAmendmentRefundState state, ApplicationDbContext context) : IOrderAmendmentRefundProvider
{
    public AmendmentRefundProviderContext ReadContext() =>
        new(FakeAmendmentRefundState.AccountId, false);

    public async Task<AmendmentRefundEvidence> CreateAsync(
        AmendmentRefundRequest request, CancellationToken cancellationToken)
    {
        state.CreateCalls++;
        state.ProviderIoObservedDatabaseTransaction |= context.Database.CurrentTransaction is not null;
        state.LastRequest = request;
        var operationId = Guid.ParseExact(request.Metadata[StripeOrderAmendmentRefundProvider.OperationKey], "D");
        var legId = Guid.ParseExact(request.Metadata[StripeOrderAmendmentRefundProvider.LegKey], "D");
        var attemptId = Guid.ParseExact(request.Metadata[StripeOrderAmendmentRefundProvider.AttemptKey], "D");
        state.ProviderRequestWasDurableBeforeCreate = context.Database.CurrentTransaction is null
            && await context.OrderAmendmentRefundEvidence.AsNoTracking().AnyAsync(value =>
                value.RefundLegId == legId && value.RefundAttemptId == attemptId
                && value.Kind == RestaurantSystem.Domain.Common.Enums.OrderAmendmentRefundEvidenceKind.ProviderRequest,
                cancellationToken)
            && await context.OrderAmendmentResolutionOperations.AsNoTracking().AnyAsync(value =>
                value.Id == operationId && value.State == RestaurantSystem.Domain.Common.Enums
                    .OrderAmendmentResolutionOperationState.Processing, cancellationToken);

        var previous = state.ReadRefunds().SingleOrDefault(value =>
            value.Metadata.TryGetValue(StripeOrderAmendmentRefundProvider.AttemptKey, out var saved)
            && saved == attemptId.ToString("D"));
        if (previous is not null)
            return previous;

        var status = state.NextCreateStatus();
        var response = new AmendmentRefundEvidence(
            $"re_amendment_{attemptId:N}", request.ChargeId, request.IntentId, request.AmountMinor,
            request.Currency.ToLowerInvariant(), status, ReadContext(),
            new Dictionary<string, string>(request.Metadata, StringComparer.Ordinal));
        state.AddRefund(response);
        if (state.CreateCalls == 1 && state.CreateResponseGate is { } responseGate)
        {
            responseGate.Prepare(response);
            await responseGate.WaitForReleaseAsync(cancellationToken);
        }
        if (state.LoseFirstCreateResponse && state.CreateCalls == 1)
            throw new TimeoutException("Simulated lost response after the provider accepted the refund.");
        return response;
    }

    public async Task<IReadOnlyList<AmendmentRefundEvidence>> ListForChargeAsync(
        string chargeId, CancellationToken cancellationToken)
    {
        var listCall = state.NextListCall();
        state.ProviderIoObservedDatabaseTransaction |= context.Database.CurrentTransaction is not null;
        IReadOnlyList<AmendmentRefundEvidence> result = state.ReadRefunds()
            .Where(value => value.ChargeId == chargeId).ToArray();
        if (state.ListResponseGates.TryGetValue(listCall, out var responseGate))
        {
            responseGate.Prepare(result);
            await responseGate.WaitForReleaseAsync(cancellationToken);
        }
        return result;
    }
}

internal sealed class FixedAdminCurrentUserService(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;
    public string? UserName => "admin";
    public string? Email => "admin@example.test";
    public UserRole? Role => UserRole.Admin;
    public bool IsAuthenticated => true;
    public bool IsApiToken => false;
    public bool IsAdmin => true;

    public Task<ApplicationUser?> GetUserAsync() => Task.FromResult<ApplicationUser?>(null);
}

internal sealed class FakeAmendmentCheckoutEvidenceReader(FakeAmendmentRefundState state)
    : IAccountCheckoutEvidenceReader
{
    public Task<AccountCheckoutCanonicalEvidence> ReadAsync(
        AccountCheckoutJournal journal, bool allowOriginalCreateRetry,
        CancellationToken cancellationToken)
    {
        var providerContext = new AccountStripeContext(journal.ProviderAccountId, journal.ProviderLiveMode);
        var metadata = new Dictionary<string, string>
        {
            [AccountStripeCheckoutClient.AttemptMetadataKey] = journal.AttemptId.ToString("D"),
            [AccountStripeCheckoutClient.SchemaMetadataKey] = AccountStripeCheckoutClient.SchemaVersion
        };
        var refunds = state.ReadRefunds().Where(value => value.ChargeId == journal.ProviderChargeId).ToArray();
        var refunded = state.CanonicalRefundedMinorOverride
            ?? refunds.Where(value => value.Status == "succeeded").Sum(value => value.AmountMinor);
        return Task.FromResult(new AccountCheckoutCanonicalEvidence(new AccountStripeSession
        {
            Id = journal.ProviderSessionId!,
            Context = providerContext,
            Status = "complete",
            PaymentStatus = "paid",
            AmountMinor = journal.AmountMinor,
            Currency = journal.Currency,
            IntentId = journal.ProviderIntentId,
            Metadata = metadata,
            ClientReferenceId = journal.AttemptId.ToString("D")
        }, new AccountStripeIntent
        {
            Id = journal.ProviderIntentId!,
            Context = providerContext,
            Status = "succeeded",
            AmountMinor = journal.AmountMinor,
            ReceivedMinor = journal.AmountMinor,
            Currency = journal.Currency,
            ChargeId = journal.ProviderChargeId,
            Metadata = metadata
        }, new AccountStripeCharge
        {
            Id = journal.ProviderChargeId!,
            Context = providerContext,
            IntentId = journal.ProviderIntentId!,
            Status = "succeeded",
            AmountMinor = journal.AmountMinor,
            CapturedMinor = journal.ProviderCapturedMinor,
            RefundedMinor = refunded,
            Currency = journal.Currency,
            Paid = true,
            Captured = true,
            Disputed = false
        })
        { Refunds = refunds });
    }
}
