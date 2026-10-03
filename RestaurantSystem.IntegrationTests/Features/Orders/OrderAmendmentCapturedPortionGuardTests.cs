using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentCapturedPortionGuardTests
{
    private static readonly Guid OrderId = Guid.Parse("b1111111-1111-4111-8111-111111111111");
    private static readonly Guid ItemId = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly AccountMoney Money = new("CHF");

    [Fact]
    public void Separate_six_and_four_franc_contributions_can_pay_the_same_ten_franc_unit()
    {
        var source = Source();
        var first = Allocation(600);
        var second = Allocation(400);

        var action = () => OrderAmendmentCapturedPortionGuard.RequireConserved(source, [first, second], Money);

        action.Should().NotThrow();
        first.AmountMinor.Should().Be(600);
        second.AmountMinor.Should().Be(400);
        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public void One_cent_over_the_same_unit_is_rejected_even_when_other_food_is_unpaid()
    {
        var source = Source();
        source.Total = 20;
        source.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            Quantity = 1,
            ItemTotal = 10,
            CreatedBy = nameof(OrderAmendmentCapturedPortionGuardTests)
        });

        var action = () => OrderAmendmentCapturedPortionGuard.RequireConserved(
            source, [Allocation(600), Allocation(401)], Money);

        action.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Repeated_allocation_identity_is_rejected_even_when_its_amount_fits_the_unit()
    {
        var first = Allocation(400);
        var repeated = Allocation(400);
        repeated.Id = first.Id;

        var action = () => OrderAmendmentCapturedPortionGuard.RequireConserved(Source(), [first, repeated], Money);

        action.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData("order")]
    [InlineData("item")]
    [InlineData("attempt")]
    [InlineData("payment")]
    [InlineData("range")]
    [InlineData("amount")]
    [InlineData("overflow")]
    public void Foreign_or_malformed_capture_evidence_is_held(string defect)
    {
        var allocation = Allocation(400);
        switch (defect)
        {
            case "order": allocation.OrderId = Guid.NewGuid(); break;
            case "item": allocation.OrderItemId = Guid.NewGuid(); break;
            case "attempt": allocation.AttemptId = Guid.Empty; break;
            case "payment": allocation.OrderPaymentId = null; break;
            case "range": allocation.StartOrdinal = 2; break;
            case "amount": allocation.AmountMinor = 399; break;
            case "overflow": allocation.UnitCount = int.MaxValue; allocation.MinorPerUnit = long.MaxValue; break;
        }

        var action = () => OrderAmendmentCapturedPortionGuard.RequireConserved(Source(), [allocation], Money);

        action.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Food_and_tip_portions_are_conserved_in_separate_frozen_scopes()
    {
        var source = Source();
        source.Total = 12;
        source.Tip = 2;
        var tip = Allocation(200);
        tip.OrderItemId = null;

        var action = () => OrderAmendmentCapturedPortionGuard.RequireConserved(source, [Allocation(1000), tip], Money);

        action.Should().NotThrow();
        tip.MinorPerUnit = 201;
        tip.AmountMinor = 201;
        action.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Extreme_quantity_is_checked_without_materializing_every_unit()
    {
        var source = Source();
        source.Total = int.MaxValue / 100m;
        source.Items.Single().Quantity = int.MaxValue;
        source.Items.Single().ItemTotal = source.Total;
        var first = Allocation(int.MaxValue);
        first.UnitCount = int.MaxValue;
        first.MinorPerUnit = 1;

        var action = () => OrderAmendmentCapturedPortionGuard.RequireConserved(source, [first], Money);

        action.Should().NotThrow();
    }

    private static Order Source() => new()
    {
        Id = OrderId,
        Total = 10,
        CreatedBy = nameof(OrderAmendmentCapturedPortionGuardTests),
        Items = [new OrderItem
        {
            Id = ItemId, OrderId = OrderId, Quantity = 1, ItemTotal = 10,
            CreatedBy = nameof(OrderAmendmentCapturedPortionGuardTests)
        }]
    };

    private static AccountPaymentAllocation Allocation(long amount) => new()
    {
        Id = Guid.NewGuid(),
        AttemptId = Guid.NewGuid(),
        OrderId = OrderId,
        OrderItemId = ItemId,
        OrderPaymentId = Guid.NewGuid(),
        StartOrdinal = 1,
        UnitCount = 1,
        MinorPerUnit = amount,
        AmountMinor = amount,
        CreatedBy = nameof(OrderAmendmentCapturedPortionGuardTests)
    };
}
