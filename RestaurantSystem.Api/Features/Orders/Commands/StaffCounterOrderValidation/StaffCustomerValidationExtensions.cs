using System.Globalization;
using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;

internal static class StaffCustomerValidationExtensions
{
    internal static void ValidateStaffCustomer(
        this AbstractValidator<StaffCounterOrderRequest> validator,
        int maximumPointsPerRedemption)
    {
        validator.RuleFor(request => request)
            .Must(request => !request.CustomerUserId.HasValue
                || !request.CustomerId.HasValue
                || request.CustomerUserId == request.CustomerId)
            .WithMessage("CustomerUserId and CustomerId must identify the same customer.");
        validator.RuleFor(request => request.PointsToRedeem)
            .GreaterThanOrEqualTo(0)
            .When(request => request.PointsToRedeem.HasValue)
            .WithMessage("PointsToRedeem cannot be negative.");
        validator.RuleFor(request => request.PointsToRedeem)
            .LessThanOrEqualTo(maximumPointsPerRedemption)
            .When(request => request.PointsToRedeem.HasValue)
            .WithMessage(
                $"Cannot redeem more than {maximumPointsPerRedemption.ToString("N0", CultureInfo.InvariantCulture)} points at once.");
        validator.RuleFor(request => request.PointsToRedeem)
            .Must((request, points) => points is null || points <= 0 || request.EffectiveCustomerUserId.HasValue)
            .When(request => request.PointsToRedeem.HasValue)
            .WithMessage("Points redemption requires a registered customer.");
    }
}
