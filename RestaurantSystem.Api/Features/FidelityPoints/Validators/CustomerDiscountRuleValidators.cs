using FluentValidation;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;

namespace RestaurantSystem.Api.Features.FidelityPoints.Validators;

public class CreateCustomerDiscountRuleValidator : AbstractValidator<CreateCustomerDiscountRuleDto>
{
    public CreateCustomerDiscountRuleValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Discount name is required")
            .MaximumLength(200).WithMessage("Discount name cannot exceed 200 characters");

        RuleFor(x => x.DiscountType)
            .NotEmpty().WithMessage("Discount type is required")
            .Must(x => x == "Percentage" || x == "FixedAmount")
            .WithMessage("Discount type must be 'Percentage' or 'FixedAmount'");

        RuleFor(x => x.DiscountValue)
            .GreaterThan(0).WithMessage("Discount value must be positive");

        RuleFor(x => x.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(x => x.DiscountType == "Percentage")
            .WithMessage("Percentage discount cannot exceed 100%");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinOrderAmount.HasValue)
            .WithMessage("Minimum order amount must be non-negative");

        RuleFor(x => x.MaxOrderAmount)
            .GreaterThan(x => x.MinOrderAmount ?? 0)
            .When(x => x.MaxOrderAmount.HasValue && x.MinOrderAmount.HasValue)
            .WithMessage("Maximum order amount must be greater than minimum order amount");

        RuleFor(x => x.MaxUsageCount)
            .GreaterThan(0)
            .When(x => x.MaxUsageCount.HasValue)
            .WithMessage("Maximum usage count must be positive");

        RuleFor(x => x.ValidUntil)
            .GreaterThan(x => x.ValidFrom ?? DateTime.MinValue)
            .When(x => x.ValidUntil.HasValue && x.ValidFrom.HasValue)
            .WithMessage("Valid until date must be after valid from date");
    }
}

public class UpdateCustomerDiscountRuleValidator : AbstractValidator<UpdateCustomerDiscountRuleDto>
{
    public UpdateCustomerDiscountRuleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Discount name is required")
            .MaximumLength(200).WithMessage("Discount name cannot exceed 200 characters");

        RuleFor(x => x.DiscountType)
            .NotEmpty().WithMessage("Discount type is required")
            .Must(x => x == "Percentage" || x == "FixedAmount")
            .WithMessage("Discount type must be 'Percentage' or 'FixedAmount'");

        RuleFor(x => x.DiscountValue)
            .GreaterThan(0).WithMessage("Discount value must be positive");

        RuleFor(x => x.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(x => x.DiscountType == "Percentage")
            .WithMessage("Percentage discount cannot exceed 100%");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinOrderAmount.HasValue)
            .WithMessage("Minimum order amount must be non-negative");

        RuleFor(x => x.MaxOrderAmount)
            .GreaterThan(x => x.MinOrderAmount ?? 0)
            .When(x => x.MaxOrderAmount.HasValue && x.MinOrderAmount.HasValue)
            .WithMessage("Maximum order amount must be greater than minimum order amount");

        RuleFor(x => x.MaxUsageCount)
            .GreaterThan(0)
            .When(x => x.MaxUsageCount.HasValue)
            .WithMessage("Maximum usage count must be positive");

        RuleFor(x => x.ValidUntil)
            .GreaterThan(x => x.ValidFrom ?? DateTime.MinValue)
            .When(x => x.ValidUntil.HasValue && x.ValidFrom.HasValue)
            .WithMessage("Valid until date must be after valid from date");
    }
}
