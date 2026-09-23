using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.FidelityPoints.Dtos;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.FidelityPoints.Validators;

public class RedeemPointsRequestValidator : AbstractValidator<RedeemPointsRequestDto>
{
    public RedeemPointsRequestValidator(IOptions<FidelitySettings> settings)
    {
        RuleFor(x => x.PointsToRedeem)
            .GreaterThan(0).WithMessage("Points to redeem must be positive")
            .LessThanOrEqualTo(settings.Value.MaximumPointsPerRedemption)
            .WithMessage(
                $"Cannot redeem more than {settings.Value.MaximumPointsPerRedemption.ToString("N0", CultureInfo.InvariantCulture)} points at once");
    }
}
