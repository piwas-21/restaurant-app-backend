using FluentAssertions;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountPaymentStateTests
{
    [Theory]
    [InlineData(AccountPaymentState.Quoted, true, false)]
    [InlineData(AccountPaymentState.Reserved, true, true)]
    [InlineData(AccountPaymentState.Starting, false, true)]
    [InlineData(AccountPaymentState.Processing, false, true)]
    [InlineData(AccountPaymentState.CancelRequested, false, true)]
    [InlineData(AccountPaymentState.ReconciliationRequired, false, true)]
    [InlineData(AccountPaymentState.Captured, false, false)]
    [InlineData(AccountPaymentState.Released, false, false)]
    [InlineData(AccountPaymentState.Failed, false, false)]
    [InlineData((AccountPaymentState)999, false, true)]
    public void Local_expiry_cannot_free_debt_after_a_provider_request_may_have_escaped(
        AccountPaymentState state, bool canRelease, bool blocksClose)
    {
        state.CanReleaseLocally().Should().Be(canRelease);
        state.BlocksClose().Should().Be(blocksClose);
        state.HoldsReservation().Should().Be(blocksClose);
    }
}
