using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// #340. The two columns a cancellation writes the reason to are both varchar(500):
/// <c>Order.CancellationReason</c> and <c>OrderStatusHistory.Notes</c>. The validator carried a
/// floor (5 characters) but no ceiling, so a longer reason surfaced as Postgres 22001 out of
/// <c>SaveChangesAsync</c> — a 500 with the order left open — instead of the stateable 400 the
/// MaximumLength rule now produces.
/// </summary>
public class CancelOrderCommandValidatorTests
{
    private readonly CancelOrderCommandValidator _validator = new();

    [Fact]
    public void Validate_ReasonLongerThan500Characters_FailsWithLengthError()
    {
        var command = new CancelOrderCommand
        {
            OrderId = Guid.NewGuid(),
            CancellationReason = new string('x', 501)
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(CancelOrderCommand.CancellationReason) &&
            e.ErrorMessage == "Cancellation reason cannot exceed 500 characters");
    }

    /// <summary>The accept boundary: exactly the column limit — one more character is what used to 500.</summary>
    [Fact]
    public void Validate_ReasonAtExactly500Characters_Passes()
    {
        var command = new CancelOrderCommand
        {
            OrderId = Guid.NewGuid(),
            CancellationReason = new string('x', 500)
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TypicalReason_StillPasses()
    {
        var command = new CancelOrderCommand
        {
            OrderId = Guid.NewGuid(),
            CancellationReason = "Customer changed their mind"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
