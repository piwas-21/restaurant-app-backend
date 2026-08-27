using FluentValidation;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.ApiTokens.Commands.CreateApiTokenCommand;

public class CreateApiTokenCommandValidator : AbstractValidator<CreateApiTokenCommand>
{
    /// <summary>Longest life a token may be given. A year is already generous for a machine.</summary>
    private const int MaxExpiryDays = 365;

    public CreateApiTokenCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("A token name is required.")
            .MaximumLength(100).WithMessage("Token name must be 100 characters or fewer.");

        RuleFor(x => x.Scopes)
            .NotEmpty().WithMessage("At least one scope is required.");

        // An unknown scope is REFUSED, not dropped: silently issuing a token that grants nothing
        // sends the client away to debug a 403 whose cause was a typo we already saw.
        RuleForEach(x => x.Scopes)
            .Must(ApiTokenScopes.IsKnown)
            .WithMessage("'{PropertyValue}' is not a known scope.");

        RuleFor(x => x.ExpiresInDays)
            .InclusiveBetween(1, MaxExpiryDays)
            .WithMessage($"Expiry must be between 1 and {MaxExpiryDays} days.");
    }
}
