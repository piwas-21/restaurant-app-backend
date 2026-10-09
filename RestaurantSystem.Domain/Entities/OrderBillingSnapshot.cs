using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantSystem.Domain.Entities;

public enum OrderBillingTaxTreatment
{
    NotApplied = 1,
    Included = 2,
    Excluded = 3
}

/// <summary>Immutable accepted money, raw pricing evidence, tax policy and loyalty for one native order.</summary>
public sealed class OrderBillingSnapshot : Entity
{
    public Guid OrderId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PricingPolicyVersion { get; set; } = string.Empty;
    public string ComponentQuantizationPolicyVersion { get; set; } = string.Empty;
    public string EarningBasisPolicyVersion { get; set; } = string.Empty;
    public long GrossFoodMinor { get; set; }
    public long TaxMinor { get; set; }
    public long DeliveryFeeMinor { get; set; }
    public long ChargedDeliveryFeeMinor { get; set; }
    public long OrderDiscountMinor { get; set; }
    public long CustomerDiscountMinor { get; set; }
    public long CourtesyRoundingMinor { get; set; }
    public int RedeemedPoints { get; set; }
    public long RedemptionDiscountMinor { get; set; }
    public long PayableFoodMinor { get; set; }
    public long TipMinor { get; set; }
    public long TotalMinor { get; set; }
    public long FoodReconciliationMinor { get; set; }
    public decimal RawTaxAmount { get; set; }
    public decimal RawOrderDiscountAmount { get; set; }
    public decimal RawCustomerDiscountAmount { get; set; }
    public decimal RawRedemptionDiscountAmount { get; set; }
    public decimal RawCourtesyRoundingAmount { get; set; }
    public long EarningBasisMinor { get; set; }
    public OrderBillingEarningDisposition? EarningDisposition { get; set; }
    [NotMapped]
    public OrderBillingEarningDisposition EffectiveEarningDisposition => EarningDisposition
        ?? (EarnedPointsCandidate.HasValue
            ? OrderBillingEarningDisposition.Evaluated
            : OrderBillingEarningDisposition.Unevaluated);
    public int? EarnedPointsCandidate { get; set; }
    public string? EarningEvaluationVersion { get; set; }
    public string? EarningRuleSetFingerprint { get; set; }
    public Guid? EarningRuleId { get; set; }
    public string? EarningRuleName { get; set; }
    public long? EarningRuleMinimumMinor { get; set; }
    public long? EarningRuleMaximumMinor { get; set; }
    public int? EarningRulePoints { get; set; }
    public int? EarningRulePriority { get; set; }
    public Guid? RedemptionTransactionId { get; set; }
    public TransactionType? RedemptionTransactionType { get; set; }
    public int? RedemptionTransactionPoints { get; set; }
    public decimal? RedemptionTransactionOrderTotal { get; set; }
    public DateTime? RedemptionTransactionCreatedAt { get; set; }
    public string TaxCategory { get; set; } = "none";
    public int TaxRateBasisPoints { get; set; }
    public OrderBillingTaxTreatment TaxTreatment { get; set; } = OrderBillingTaxTreatment.NotApplied;
}
