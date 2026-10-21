using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerTablePaymentAccountShapeTests
{
    private static readonly Guid VisitId = Guid.Parse("655a2173-01e5-4cb4-aa8d-42f3dacaf457");
    private static readonly Guid OrderId = Guid.Parse("b19159fe-ef9b-4b91-877b-2697c6a1900e");
    private static AccountPaymentAccountDto Account() => new(VisitId, TableServiceSessionStatus.Open, 7, "CHF",
        1400, 0, 1400, 0, [new(OrderId, null, 1, 1, 1400, 1400)],
        [new(OrderId, null, 1, 1, 1400, 1400)], null, [], new(250, 100), 1);

    [Fact]
    public void A_frozen_server_ledger_is_not_repriced_or_reallocated()
    {
        var account = Account();
        var before = System.Text.Json.JsonSerializer.Serialize(account);
        CustomerTablePaymentAccountShape.Require(account, VisitId, "CHF");
        System.Text.Json.JsonSerializer.Serialize(account).Should().Be(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Server_owned_equal_or_custom_plan_amounts_are_preserved(bool custom)
    {
        var account = Account() with { ActiveEqualSharePlan = Plan(custom), Limits = new(250, 100, new("CHF", 50, 100_000)) };
        CustomerTablePaymentAccountShape.Require(account, VisitId, "CHF");
        account.ActiveEqualSharePlan.Slots.Select(value => value.AmountMinor).Should().Equal(700, 700);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("revision")]
    [InlineData("total")]
    [InlineData("slots")]
    [InlineData("ordinal")]
    [InlineData("claim")]
    [InlineData("custom")]
    public void A_plan_cannot_smuggle_an_unsupported_native_identity_or_amount(string changed)
    {
        var plan = Plan(false);
        plan = changed switch
        {
            "identity" => plan with { PlanId = Guid.Empty },
            "revision" => plan with { AccountRevision = 9_007_199_254_740_992 },
            "total" => plan with { TotalMinor = -1 },
            "slots" => plan with { Slots = [] },
            "ordinal" => plan with { Slots = [new(1, 700, null, true), new(1, 700, null, true)] },
            "claim" => plan with { Slots = [new(1, 700, (AccountPaymentState)99, true), new(2, 700, null, true)] },
            "custom" => plan with { IsCustom = true, CustomAmountsMinor = [9_007_199_254_740_992, 700] },
            _ => throw new ArgumentException("Unknown scenario.", nameof(changed))
        };
        var read = () => CustomerTablePaymentAccountShape.Require(Account() with { ActiveEqualSharePlan = plan }, VisitId, "CHF");
        read.Should().Throw<ConflictException>();
    }

    private static AccountPaymentEqualShareSummaryDto Plan(bool custom) => new(
        Guid.Parse("1830be78-40c6-41ee-a71c-f5170d2a751f"), 7, 1400, 2, "CHF", false,
        [new(1, 700, null, true), new(2, 700, null, true)], Account().AvailableAllocations, custom, custom ? [700, 700] : null);

    [Theory]
    [InlineData("revision")]
    [InlineData("outstanding")]
    [InlineData("captured")]
    [InlineData("allocation")]
    [InlineData("limit")]
    [InlineData("segment-count")]
    [InlineData("foreign-operation")]
    [InlineData("currency")]
    public void Hostile_snapshots_are_refused_before_JSON_can_lose_identity_or_money(string changed)
    {
        const long inexact = 9_007_199_254_740_992;
        var account = Account();
        account = changed switch
        {
            "revision" => account with { AccountRevision = inexact },
            "outstanding" => account with { OutstandingMinor = inexact },
            "captured" => account with { CapturedAccountPaymentMinor = -1 },
            "allocation" => account with { AvailableAllocations = [new(OrderId, null, 1, 1, inexact, inexact)] },
            "limit" => account with { Limits = new(250, 100, new("CHF", 50, inexact)) },
            "segment-count" => account with { OutstandingAllocations = Enumerable.Repeat(account.OutstandingAllocations[0], 5001).ToArray() },
            "foreign-operation" => account with
            {
                ActiveAttempts = [new(Guid.NewGuid(), AccountPaymentState.Reserved,
                1, PaymentMethod.OnlinePayment, 700, "CHF", null, null, null, false)]
            },
            "currency" => account with { Currency = "EUR" },
            _ => throw new ArgumentException("Unknown scenario.", nameof(changed))
        };
        var read = () => CustomerTablePaymentAccountShape.Require(account, VisitId, "CHF");
        read.Should().Throw<ConflictException>();
    }
}
