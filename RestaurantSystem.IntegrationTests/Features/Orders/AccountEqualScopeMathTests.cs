using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountEqualScopeMathTests
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    [Fact]
    public void OneHundredForThreeUsesFixed3334And3333SharesEvenWhenPaidOutOfOrder()
    {
        IReadOnlyList<AccountDebtSegment> scope = [new(OrderId, ItemId, 1, 2, 5000)];
        var first = AccountEqualScopeMath.ForShare(scope, 3, 1);
        var second = AccountEqualScopeMath.ForShare(scope, 3, 2);
        var third = AccountEqualScopeMath.ForShare(scope, 3, 3);
        AccountDebtMath.Total(first).Should().Be(3334);
        AccountDebtMath.Total(second).Should().Be(3333);
        AccountDebtMath.Total(third).Should().Be(3333);
        first.Should().Equal(new AccountDebtSegment(OrderId, ItemId, 1, 1, 3334));
        second.Should().Equal(new AccountDebtSegment(OrderId, ItemId, 1, 1, 1666),
            new AccountDebtSegment(OrderId, ItemId, 2, 1, 1667));
        third.Should().Equal(new AccountDebtSegment(OrderId, ItemId, 2, 1, 3333));
        var afterThird = AccountDebtMath.Subtract(scope, third);
        AccountDebtMath.Total(afterThird).Should().Be(6667);
        AccountDebtMath.Subtract(afterThird, first.Concat(second).ToArray()).Should().BeEmpty();
    }

    [Fact]
    public void LaterRoundDoesNotEnterTheExistingPlanScope()
    {
        IReadOnlyList<AccountDebtSegment> reviewed = [new(OrderId, ItemId, 1, 1, 10000)];
        var laterRound = new AccountDebtSegment(Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1200);
        var third = AccountEqualScopeMath.ForShare(reviewed, 3, 3);
        third.Should().Equal(new AccountDebtSegment(OrderId, ItemId, 1, 1, 3333));
        var currentDebt = reviewed.Append(laterRound).ToArray();
        AccountDebtMath.Total(AccountDebtMath.Subtract(currentDebt, third)).Should().Be(7867);
    }

    [Fact]
    public void ZeroAmountShareAndInvalidOrdinalAreRefused()
    {
        IReadOnlyList<AccountDebtSegment> scope = [new(OrderId, ItemId, 1, 1, 1)];
        var emptyShare = () => AccountEqualScopeMath.ForShare(scope, 2, 2);
        var invalidOrdinal = () => AccountEqualScopeMath.ForShare(scope, 2, 3);
        emptyShare.Should().Throw<BadRequestException>();
        invalidOrdinal.Should().Throw<BadRequestException>();
    }
}
