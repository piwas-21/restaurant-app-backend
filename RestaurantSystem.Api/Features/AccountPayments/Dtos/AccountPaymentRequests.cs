using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record AccountPaymentUnitSelection(Guid OrderId, Guid OrderItemId, int Ordinal);

public sealed record CreateAccountPaymentQuoteRequest
{
    [JsonRequired]
    public Guid OperationId { get; init; }

    [JsonRequired]
    public long ExpectedAccountRevision { get; init; }

    [JsonRequired]
    public AccountPaymentMode Mode { get; init; }

    [JsonRequired]
    public PaymentMethod PaymentMethod { get; init; }

    public IReadOnlyList<AccountPaymentUnitSelection> SelectedUnits { get; init; } = [];
    public long? AmountMinor { get; init; }
    public Guid? EqualSharePlanId { get; init; }
    public int? EqualShareOrdinal { get; init; }
    public Guid? CustomSharePlanId { get; init; }
    public int? CustomShareOrdinal { get; init; }
    public long TipMinor { get; init; }
}

public sealed record CreateAccountEqualSharePlanRequest
{
    [JsonRequired]
    public Guid OperationId { get; init; }

    [JsonRequired]
    public long ExpectedAccountRevision { get; init; }

    [JsonRequired]
    public int ShareCount { get; init; }

    public Guid? SupersedesPlanId { get; init; }
    public IReadOnlyList<long> CustomAmountsMinor { get; init; } = [];
}

public sealed record ReserveAccountPaymentRequest
{
    [JsonRequired]
    public int ExpectedVersion { get; init; }

    [JsonRequired]
    public long ExpectedAccountRevision { get; init; }
}

public sealed record ReleaseAccountPaymentRequest
{
    [JsonRequired]
    public int ExpectedVersion { get; init; }
}
