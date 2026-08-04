using FluentAssertions;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins the order lifecycle table in
/// <see cref="OrderStatusTransitions.IsValid"/>.
///
/// No database: the table is a pure function of two enum values, so these run
/// the whole 10 × 10 grid directly. That exhaustiveness is the point — issue
/// #287 was a state refused by OMISSION, and an omission is exactly what a
/// hand-picked list of cases fails to notice.
/// </summary>
public class OrderStatusTransitionTableTests
{
    private static readonly OrderStatus[] AllStatuses = Enum.GetValues<OrderStatus>();

    /// <summary>
    /// The heart of #287. <c>Delivered</c> reads like an oversight because it
    /// has no exit — but the reason it has no exit is that it has no ENTRANCE.
    /// Adding a rule out of it, which is the obvious fix, would change nothing.
    ///
    /// This fails the moment someone gives either state an entrance, which is
    /// precisely when the "unreachable" comments beside them stop being true
    /// and the exit has to be thought about.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Refunded)]
    public void NoTransitionTargets_UnreachableStatuses(OrderStatus unreachable)
    {
        var entrances = AllStatuses
            .Where(from => IsValid(from, unreachable))
            .ToList();

        entrances.Should().BeEmpty(
            "{0} is documented as unreachable, so nothing may transition INTO it — " +
            "give it an entrance and it needs an exit in the same change", unreachable);
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Refunded)]
    public void TerminalStatuses_AllowNoTransitionOut(OrderStatus terminal)
    {
        var exits = AllStatuses
            .Where(to => IsValid(terminal, to))
            .ToList();

        exits.Should().BeEmpty("{0} is terminal", terminal);
    }

    [Fact]
    public void NoStatusTransitionsToItself()
    {
        // A no-op transition would still write a history row and fire the
        // status-changed notification, so "Confirmed -> Confirmed" is not
        // harmless. None of the arms allow it today; keep it that way.
        AllStatuses.Where(s => IsValid(s, s)).Should().BeEmpty();
    }

    [Fact]
    public void EveryNonTerminalStatusCanBeCancelled()
    {
        // A staff member must always be able to kill a live order. If a new
        // non-terminal state is added without a Cancelled arm, an order can
        // strand there.
        var live = new[]
        {
            OrderStatus.Pending,
            OrderStatus.PendingApproval,
            OrderStatus.Confirmed,
            OrderStatus.Preparing,
            OrderStatus.Ready,
            OrderStatus.OutForDelivery,
        };

        live.Where(s => !IsValid(s, OrderStatus.Cancelled))
            .Should().BeEmpty("every live order must be cancellable");
    }

    [Fact]
    public void HappyPathToCompletion_IsWalkable()
    {
        // Guards the grid tests above: they would all pass vacuously if the
        // table returned false for everything.
        IsValid(OrderStatus.Pending, OrderStatus.Confirmed).Should().BeTrue();
        IsValid(OrderStatus.Confirmed, OrderStatus.Preparing).Should().BeTrue();
        IsValid(OrderStatus.Preparing, OrderStatus.Ready).Should().BeTrue();
        IsValid(OrderStatus.Ready, OrderStatus.Completed).Should().BeTrue();
        IsValid(OrderStatus.Ready, OrderStatus.OutForDelivery).Should().BeTrue();
        IsValid(OrderStatus.OutForDelivery, OrderStatus.Completed).Should().BeTrue();
    }

    private static bool IsValid(OrderStatus from, OrderStatus to)
        => OrderStatusTransitions.IsValid(from, to);
}
