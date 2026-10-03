using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public sealed class TableGuestEffectiveBillingTests
{
    [Fact]
    public void Guest_round_explains_original_charge_credit_and_payable_total()
    {
        var order = new OrderDto
        {
            Id = Guid.NewGuid(),
            OrderNumber = "CREDIT-ROUND",
            Total = 15m,
            PayableTotal = 5m,
            BillingCreditAmount = 10m,
            RemainingAmount = 3m,
            TotalPaid = 2m
        };
        var bill = new TableBillDto
        {
            ServiceSessionId = Guid.NewGuid(),
            AccountRevision = 7,
            Currency = "CHF",
            Total = 5m,
            OriginalTotal = 15m,
            BillingCreditAmount = 10m,
            TotalPaid = 2m,
            Remaining = 3m,
            Orders = [order]
        };

        var account = TableGuestAccountReader.Project(bill);

        account.Total.Should().Be(5m);
        account.OriginalTotal.Should().Be(15m);
        account.BillingCreditAmount.Should().Be(10m);
        account.Remaining.Should().Be(3m);
        account.Orders.Should().ContainSingle();
        account.Orders[0].Total.Should().Be(15m);
        account.Orders[0].PayableTotal.Should().Be(5m);
        account.Orders[0].BillingCreditAmount.Should().Be(10m);
        account.Orders[0].RemainingAmount.Should().Be(3m);
    }

    [Fact]
    public void Unchanged_guest_accounts_omit_additive_billing_fields()
    {
        var bill = new TableBillDto
        {
            ServiceSessionId = Guid.NewGuid(),
            AccountRevision = 1,
            Total = 15m,
            Orders = [new OrderDto { Id = Guid.NewGuid(), Total = 15m }]
        };
        var account = TableGuestAccountReader.Project(bill);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(account,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        json.RootElement.TryGetProperty("originalTotal", out _).Should().BeFalse();
        json.RootElement.TryGetProperty("billingCreditAmount", out _).Should().BeFalse();
        var round = json.RootElement.GetProperty("orders")[0];
        round.TryGetProperty("payableTotal", out _).Should().BeFalse();
        round.TryGetProperty("billingCreditAmount", out _).Should().BeFalse();
    }
}
