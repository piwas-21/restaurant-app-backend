using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerTablePaymentIntentDto
{
    [JsonRequired] public int SchemaVersion { get; init; }
    [JsonRequired] public Guid TenantId { get; init; }
    [JsonRequired] public Guid OperationId { get; init; }
    [JsonRequired] public Guid ServiceSessionId { get; init; }
    [JsonRequired] public long ExpectedAccountRevision { get; init; }
    [JsonRequired] public AccountPaymentMode Mode { get; init; }
    public IReadOnlyList<AccountPaymentUnitSelection> SelectedUnits { get; init; } = [];
    public long? AmountMinor { get; init; }
    public Guid? EqualSharePlanId { get; init; }
    public int? EqualShareOrdinal { get; init; }
    public Guid? CustomSharePlanId { get; init; }
    public int? CustomShareOrdinal { get; init; }
}
