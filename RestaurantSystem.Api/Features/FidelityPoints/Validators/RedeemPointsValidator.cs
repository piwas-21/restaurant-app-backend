using FluentValidation;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;

namespace RestaurantSystem.Api.Features.FidelityPoints.Validators;

public class RedeemPointsRequestValidator : AbstractValidator<RedeemPointsRequestDto>
{
    public RedeemPointsRequestValidator()
    {
        RuleFor(x => x.PointsToRedeem)
            .GreaterThan(0).WithMessage("Points to redeem must be positive")
            .LessThanOrEqualTo(100000).WithMessage("Cannot redeem more than 100,000 points at once");
    }
}
