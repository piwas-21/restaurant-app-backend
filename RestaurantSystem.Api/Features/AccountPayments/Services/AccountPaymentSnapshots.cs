using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal sealed record AccountPaymentQuoteSnapshot(
    long ExpectedAccountRevision,
    AccountPaymentMode Mode,
    PaymentMethod PaymentMethod,
    long AmountMinor,
    string Currency,
    DateTime QuoteExpiresAt,
    Guid? EqualSharePlanId,
    int? EqualShareOrdinal,
    IReadOnlyList<AccountPaymentAllocationDto> Allocations,
    CashSettlementQuote? CashSettlement = null)
{
    public long TipMinor { get; init; }
    public Guid? CustomSharePlanId { get; init; }
    public int? CustomShareOrdinal { get; init; }
}

internal static class AccountPaymentSnapshots
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    internal static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new ConflictException("The saved account payment snapshot is unavailable.");

    internal static string Hash<T>(T value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value)))).ToLowerInvariant();

    internal static IReadOnlyList<AccountDebtSegment> ReadScope(string json) =>
        Deserialize<List<AccountDebtSegment>>(json);

    internal static List<AccountPaymentAllocation> Allocations(
        Guid attemptId, IReadOnlyList<AccountDebtSegment> segments, string createdBy) => segments.Select(segment =>
        new AccountPaymentAllocation
        {
            CreatedBy = createdBy,
            AttemptId = attemptId,
            OrderId = segment.OrderId,
            OrderItemId = segment.OrderItemId,
            StartOrdinal = segment.StartOrdinal,
            UnitCount = segment.Count,
            MinorPerUnit = segment.MinorPerUnit,
            AmountMinor = segment.TotalMinor
        }).ToList();

    internal static IReadOnlyList<AccountPaymentAllocationDto> ToDtos(
        IEnumerable<AccountDebtSegment> segments) => segments.Select(segment =>
        new AccountPaymentAllocationDto(segment.OrderId, segment.OrderItemId, segment.StartOrdinal,
            segment.Count, segment.MinorPerUnit, segment.TotalMinor)).ToArray();

    internal static AccountPaymentOperationDto ToOperation(AccountPaymentAttempt attempt)
    {
        var snapshot = Deserialize<AccountPaymentQuoteSnapshot>(attempt.SnapshotJson);
        if (snapshot.TipMinor != attempt.TipMinor || !HasMatchingShareMetadata(snapshot, attempt))
            throw new ConflictException("The saved payment snapshot differs from its durable tender metadata.");
        if (snapshot.CashSettlement is CashSettlementQuote frozen)
            AccountCashSettlementPolicy.RequireMatches(frozen, attempt.Currency,
                attempt.PaymentMethod, checked(attempt.AmountMinor + attempt.TipMinor));
        AccountCashCaptureReceiptPolicy.ValidateStored(attempt, snapshot);
        var receipt = attempt.CashCollectionReceipt;
        return new AccountPaymentOperationDto(
            attempt.ServiceSessionId, attempt.OperationId, attempt.State, attempt.Version,
            snapshot.ExpectedAccountRevision, snapshot.Mode, snapshot.PaymentMethod,
            snapshot.AmountMinor, snapshot.Currency, snapshot.QuoteExpiresAt,
            attempt.ReservedAt, attempt.ReservationExpiresAt, snapshot.EqualSharePlanId,
            snapshot.EqualShareOrdinal, snapshot.Allocations)
        {
            CashSettlement = snapshot.CashSettlement,
            TipMinor = snapshot.TipMinor,
            CustomSharePlanId = snapshot.CustomSharePlanId,
            CustomShareOrdinal = snapshot.CustomShareOrdinal,
            CashReceipt = receipt is null ? null : new CashCollectionReceiptDto(
                receipt.PolicyVersion, receipt.Currency, receipt.ExactAmountMinor,
                receipt.AdjustmentMinor, receipt.DueAmountMinor, receipt.ReceivedMinor,
                receipt.ChangeMinor, receipt.CapturedAt)
        };
    }

    private static bool HasMatchingShareMetadata(
        AccountPaymentQuoteSnapshot snapshot, AccountPaymentAttempt attempt) => snapshot.Mode switch
        {
            AccountPaymentMode.CustomAmount => snapshot.CustomSharePlanId == attempt.EqualSharePlanId
                && snapshot.CustomShareOrdinal == attempt.EqualShareOrdinal,
            AccountPaymentMode.Equal => snapshot.EqualSharePlanId == attempt.EqualSharePlanId
                && snapshot.EqualShareOrdinal == attempt.EqualShareOrdinal,
            _ => attempt.EqualSharePlanId is null && attempt.EqualShareOrdinal is null
        };

    internal static AccountEqualSharePlanDto ToPlan(AccountEqualSharePlan plan) => new(
        plan.ServiceSessionId, plan.Id, plan.OperationId, plan.AccountRevision,
        plan.TotalMinor, plan.ShareCount, plan.Currency, plan.CreatedAt,
        plan.InvalidatedAt, ToDtos(ReadScope(plan.ScopeJson)))
    {
        CustomAmountsMinor = plan.CustomAmountsJson is null
            ? null
            : Deserialize<List<long>>(plan.CustomAmountsJson)
    };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
