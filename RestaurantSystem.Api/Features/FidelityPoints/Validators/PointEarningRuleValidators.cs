using FluentValidation;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;

namespace RestaurantSystem.Api.Features.FidelityPoints.Validators;

public class CreatePointEarningRuleValidator : AbstractValidator<CreatePointEarningRuleDto>
{
    public CreatePointEarningRuleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Rule name is required")
            .MaximumLength(100).WithMessage("Rule name cannot exceed 100 characters");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0).WithMessage("Minimum order amount must be non-negative");

        RuleFor(x => x.MaxOrderAmount)
            .GreaterThan(x => x.MinOrderAmount)
            .When(x => x.MaxOrderAmount.HasValue)
            .WithMessage("Maximum order amount must be greater than minimum order amount");

        RuleFor(x => x.PointsAwarded)
            .GreaterThan(0).WithMessage("Points awarded must be positive");

        RuleFor(x => x.Priority)
            .GreaterThanOrEqualTo(0).WithMessage("Priority must be non-negative");
    }
}

public class UpdatePointEarningRuleValidator : AbstractValidator<UpdatePointEarningRuleDto>
{
    public UpdatePointEarningRuleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Rule name is required")
            .MaximumLength(100).WithMessage("Rule name cannot exceed 100 characters");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0).WithMessage("Minimum order amount must be non-negative");

        RuleFor(x => x.MaxOrderAmount)
            .GreaterThan(x => x.MinOrderAmount)
            .When(x => x.MaxOrderAmount.HasValue)
            .WithMessage("Maximum order amount must be greater than minimum order amount");

        RuleFor(x => x.PointsAwarded)
            .GreaterThan(0).WithMessage("Points awarded must be positive");

        RuleFor(x => x.Priority)
            .GreaterThanOrEqualTo(0).WithMessage("Priority must be non-negative");
    }
}
