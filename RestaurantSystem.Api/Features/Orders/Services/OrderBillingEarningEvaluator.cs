using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderBillingEarningEvaluator : IOrderBillingEarningEvaluator
{
    /// <summary>
    /// Lower priority numbers win, then lower minimums, then ordinal lowercase-N GUIDs.
    /// Both amount bounds are inclusive. Bump when winner semantics change.
    /// </summary>
    internal const string AlgorithmVersion = "priority-ascending-minimum-ascending-id-ascending-v1";

    private const string FingerprintFormatVersion = "active-earning-rules-canonical-json-v1";
    private readonly IPointEarningRuleService _ruleService;

    public OrderBillingEarningEvaluator(IPointEarningRuleService ruleService)
    {
        _ruleService = ruleService;
    }

    public async Task<OrderBillingEarningEvaluation> EvaluateAsync(
        decimal rawRootTotal,
        CancellationToken cancellationToken = default)
    {
        var queriedRules = await _ruleService.GetActiveRulesAsync(cancellationToken);
        var activeRules = CopyActiveRules(queriedRules);
        var winner = activeRules
            .Where(rule => rawRootTotal >= rule.MinimumOrderAmount
                && (rule.MaximumOrderAmount is null || rawRootTotal <= rule.MaximumOrderAmount.Value))
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.MinimumOrderAmount)
            .ThenBy(rule => rule.Id.ToString("N"), StringComparer.Ordinal)
            .FirstOrDefault();

        return new OrderBillingEarningEvaluation(
            winner?.PointsAwarded ?? 0,
            AlgorithmVersion,
            Fingerprint(activeRules),
            winner is null ? null : ToEvidence(winner));
    }

    private static List<RuleFacts> CopyActiveRules(List<PointEarningRule>? queriedRules)
    {
        if (queriedRules is null)
            throw InvalidRuleSet();

        var facts = new List<RuleFacts>(queriedRules.Count);
        var seenIds = new HashSet<Guid>();
        foreach (var rule in queriedRules)
        {
            if (rule is null)
                throw InvalidRuleSet();
            if (!rule.IsActive)
                continue;

            if (rule.Id == Guid.Empty || !seenIds.Add(rule.Id)
                || string.IsNullOrWhiteSpace(rule.Name)
                || rule.MinOrderAmount < 0m
                || rule.MaxOrderAmount is < 0m
                || (rule.MaxOrderAmount.HasValue && rule.MaxOrderAmount.Value < rule.MinOrderAmount)
                || rule.PointsAwarded < 0)
                throw InvalidRuleSet();

            facts.Add(new RuleFacts(rule.Id, rule.Name, rule.MinOrderAmount,
                rule.MaxOrderAmount, rule.PointsAwarded, rule.Priority));
        }

        return facts;
    }

    private static OrderBillingEarningRuleEvidence ToEvidence(RuleFacts rule) => new(
        rule.Id,
        rule.Name,
        rule.MinimumOrderAmount,
        rule.MaximumOrderAmount,
        rule.PointsAwarded,
        rule.Priority);

    private static string Fingerprint(IReadOnlyCollection<RuleFacts> rules)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(FingerprintFormatVersion);
            foreach (var rule in rules.OrderBy(rule => rule.Id.ToString("N"), StringComparer.Ordinal))
                WriteRule(writer, rule);
            writer.WriteEndArray();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static void WriteRule(Utf8JsonWriter writer, RuleFacts rule)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(rule.Id.ToString("N"));
        writer.WriteStringValue(rule.Name);
        writer.WriteStringValue(CanonicalDecimal(rule.MinimumOrderAmount));
        if (rule.MaximumOrderAmount.HasValue)
            writer.WriteStringValue(CanonicalDecimal(rule.MaximumOrderAmount.Value));
        else
            writer.WriteNullValue();
        writer.WriteNumberValue(rule.PointsAwarded);
        writer.WriteNumberValue(rule.Priority);
        writer.WriteEndArray();
    }

    private static string CanonicalDecimal(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static ConflictException InvalidRuleSet() =>
        new("Active point earning rules cannot be reconciled with a frozen order.");

    private sealed record RuleFacts(
        Guid Id,
        string Name,
        decimal MinimumOrderAmount,
        decimal? MaximumOrderAmount,
        int PointsAwarded,
        int Priority);
}
