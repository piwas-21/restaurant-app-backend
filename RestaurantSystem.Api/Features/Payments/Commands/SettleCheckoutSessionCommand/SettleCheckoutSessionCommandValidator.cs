using FluentValidation;

namespace RestaurantSystem.Api.Features.Payments.Commands.SettleCheckoutSessionCommand;

public class SettleCheckoutSessionCommandValidator : AbstractValidator<SettleCheckoutSessionCommand>
{
    /// <summary>
    /// The column is <c>varchar(255)</c> and the lookup is an equality match on a unique index, so
    /// a longer value cannot match anything that exists — bounding it here keeps an oversized
    /// string out of the query rather than letting it reach the database to find nothing.
    /// </summary>
    private const int MaxSessionIdLength = 255;

    public SettleCheckoutSessionCommandValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("Checkout session id is required")
            .MaximumLength(MaxSessionIdLength)
            .WithMessage("Checkout session id is not valid");
    }
}
