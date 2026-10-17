using System.Text.Json;
using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.BillCharges.Dtos;

namespace RestaurantSystem.Api.Features.BillCharges.Services;

internal static partial class BillChargeRules
{
    internal const int MaximumRules = 12;
    internal const decimal MaximumCharge = 10000m;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static List<BillChargeRuleDto> Read(string json) =>
        JsonSerializer.Deserialize<List<BillChargeRuleDto>>(json, JsonOptions) ?? [];

    internal static void Validate(IReadOnlyList<BillChargeRuleDto>? rules, string? currency)
    {
        if (rules is null || rules.Count > MaximumRules || rules.Any(rule => rule is null) || rules.Select(r => r.Key).Distinct().Count() != rules.Count)
            throw new BadRequestException("Provide up to twelve unique bill charge fields.");
        var money = rules.Any(r => r.Enabled) ? new AccountMoney(currency) : null;
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Key) || !KeyPattern().IsMatch(rule.Key)
                || string.IsNullOrWhiteSpace(rule.Label) || rule.Label.Trim().Length > 60
                || rule.DefaultAmount < 0 || rule.DefaultAmount > MaximumCharge
                || rule.OrderTypes is null || rule.OrderTypes.Length == 0
                || rule.OrderTypes.Any(t => t is not ("DineIn" or "Takeaway" or "Delivery"))
                || rule.Roles is null || rule.Roles.Length == 0
                || rule.Roles.Any(r => r is not ("Cashier" or "Server")))
                throw new BadRequestException("Each charge needs a key, label, valid amount, order types and staff roles.");
            money?.ToMinor(rule.DefaultAmount);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
