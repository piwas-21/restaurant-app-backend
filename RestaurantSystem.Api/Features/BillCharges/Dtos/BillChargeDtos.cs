using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.BillCharges.Dtos;

public sealed record BillChargeRuleDto(string Key, string Label, bool Enabled, decimal DefaultAmount,
    bool AllowCustomAmount, string[] OrderTypes, string[] Roles, string? Effect = null, string? Calculation = null);
public sealed record BillChargeConfigurationDto(string? Currency, IReadOnlyList<BillChargeRuleDto> Rules);
public sealed record BillChargeInputDto(string Key, decimal Amount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Effect = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Calculation = null);
public sealed record BillChargeDto(string Key, string Label, decimal Amount, string Currency);
public sealed record AddBillChargesRequest(
    [property: JsonRequired] Guid OperationId,
    [property: JsonRequired] int ExpectedVersion,
    [property: JsonRequired] List<BillChargeInputDto> Charges);
