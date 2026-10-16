using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.BillCharges.Services;

public sealed class BillChargeService(ApplicationDbContext context, ICurrentUserService currentUser) : IBillChargeService
{
    public async Task ApplyAsync(Order order, IReadOnlyList<BillChargeInputDto>? inputs, Guid operationId,
        string? currency, CancellationToken cancellationToken)
    {
        if (inputs is null) throw new BadRequestException("Bill charges must be a list.");
        if (inputs.Count == 0) return;
        if (inputs.Count > BillChargeRules.MaximumRules || inputs.Any(input => input is null) || inputs.Select(i => i.Key).Distinct().Count() != inputs.Count)
            throw new BadRequestException("Choose each bill charge once.");
        var info = await context.RestaurantInfo.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Restaurant settings are unavailable.");
        if (!string.Equals(info.Currency, currency, StringComparison.Ordinal))
            throw new ConflictException("The order currency differs from the current bill charge currency.");
        var rules = BillChargeRules.Read(info.BillChargeRulesJson).ToDictionary(r => r.Key, StringComparer.Ordinal);
        var money = new AccountMoney(currency);
        var role = currentUser.Role?.ToString();
        var existing = OrderBillChargeReader.Read(order).ToList();
        var additions = new List<OrderBillCharge>();
        foreach (var input in inputs)
        {
            if (!rules.TryGetValue(input.Key, out var rule) || !rule.Enabled
                || !rule.OrderTypes.Contains(order.Type.ToString(), StringComparer.Ordinal)
                || role != "Admin" && !rule.Roles.Contains(role ?? string.Empty, StringComparer.Ordinal))
                throw new BadRequestException("This bill charge is not enabled for this order or staff role.");
            if (input.Amount < 0 || input.Amount > BillChargeRules.MaximumCharge
                || !rule.AllowCustomAmount && input.Amount != 0 && input.Amount != rule.DefaultAmount)
                throw new BadRequestException("Use the configured bill charge amount.");
            money.ToMinor(input.Amount);
            if (input.Amount == 0) continue;
            if (existing.Any(c => c.Key == input.Key))
                throw new ConflictException("This charge is already on the bill. Refresh the order before collecting payment.");
            additions.Add(new OrderBillCharge(operationId, rule.Key, rule.Label.Trim(), input.Amount,
                money.Currency, DateTime.UtcNow, currentUser.GetAuditIdentifier()));
        }
        existing.AddRange(additions);
        order.BillChargesJson = JsonSerializer.Serialize(existing, BillChargeRules.JsonOptions);
        order.BillChargeAmount = existing.Sum(c => c.Amount);
        order.RemainingAmount = order.PayableTotal - order.TotalPaid;
    }
}
