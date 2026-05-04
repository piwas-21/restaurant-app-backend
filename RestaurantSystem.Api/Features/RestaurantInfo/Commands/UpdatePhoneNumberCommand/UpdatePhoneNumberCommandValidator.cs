using FluentValidation;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdatePhoneNumberCommand;

public class UpdatePhoneNumberCommandValidator : AbstractValidator<UpdatePhoneNumberCommand>
{
    public UpdatePhoneNumberCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Phone number ID is required");

        RuleFor(x => x.Number)
            .NotEmpty().WithMessage("Phone number is required")
            .Matches(@"^\+[1-9]\d{6,14}$").WithMessage("Phone number must be in E.164 format (e.g. +41227863333)");

        RuleFor(x => x.Label)
            .MaximumLength(100).WithMessage("Label cannot exceed 100 characters")
            .When(x => x.Label != null);

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Display order must be non-negative");
    }
}
