using FluentAssertions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountAmendmentRefundScopeShapeTests
{
    private static readonly Guid AttemptId = Guid.NewGuid();
    private static readonly Guid AllocationId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    [Fact]
    public void Unallocated_manual_till_refund_accepts_only_an_empty_scope_without_attempts()
    {
        var valid = NewLeg(OrderAmendmentRefundCustody.ManualTill, null, 1000);
        AccountAmendmentRefundIntegrity.HasValidRefundScopeShape(valid, []).Should().BeTrue();

        var withScope = Scope(1000);
        AccountAmendmentRefundIntegrity.HasValidRefundScopeShape(valid, [withScope]).Should().BeFalse();

        valid.Attempts.Add(new OrderAmendmentRefundAttempt
        { Id = Guid.NewGuid(), CreatedBy = nameof(AccountAmendmentRefundScopeShapeTests) });
        AccountAmendmentRefundIntegrity.HasValidRefundScopeShape(valid, []).Should().BeFalse();

        valid.Attempts.Clear();
        valid.ProviderIntentId = "unexpected-provider-intent";
        AccountAmendmentRefundIntegrity.HasValidRefundScopeShape(valid, []).Should().BeFalse();
    }

    [Fact]
    public void Allocated_and_provider_legs_require_exact_frozen_scope_amounts()
    {
        var allocatedTill = NewLeg(OrderAmendmentRefundCustody.ManualTill, AttemptId, 1000);
        var scope = Scope(1000);
        AccountAmendmentRefundIntegrity.HasValidRefundScopeShape(allocatedTill, [scope]).Should().BeTrue();
        AccountAmendmentRefundIntegrity.HasValidRefundScopeShape(allocatedTill, []).Should().BeFalse();

        var provider = NewLeg(OrderAmendmentRefundCustody.StripeDirect, AttemptId, 1000);
        AccountAmendmentRefundIntegrity.HasValidRefundScopeShape(provider, [scope]).Should().BeTrue();
        AccountAmendmentRefundIntegrity.HasValidRefundScopeShape(provider, []).Should().BeFalse();
        AccountAmendmentRefundIntegrity.HasValidRefundScopeShape(provider, [Scope(999)]).Should().BeFalse();
    }

    private static OrderAmendmentRefundLeg NewLeg(
        OrderAmendmentRefundCustody custody, Guid? accountAttemptId, long amountMinor) => new()
        {
            Custody = custody,
            AccountPaymentAttemptId = accountAttemptId,
            AmountMinor = amountMinor,
            CreatedBy = nameof(AccountAmendmentRefundScopeShapeTests)
        };

    private static OrderAmendmentRefundScope Scope(long amountMinor) =>
        new(AllocationId, OrderId, ItemId, 1, 1, amountMinor, amountMinor);
}
