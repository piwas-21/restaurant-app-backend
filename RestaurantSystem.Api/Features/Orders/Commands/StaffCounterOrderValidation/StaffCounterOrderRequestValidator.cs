using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;

public sealed class StaffCounterOrderRequestValidator : AbstractValidator<StaffCounterOrderRequest>
{
    public StaffCounterOrderRequestValidator()
    {
        RuleFor(request => request.Type).IsInEnum();
        RuleFor(request => request.Items).NotEmpty()
            .WithMessage("A counter order must contain at least one item.");
        RuleFor(request => request.TableNumber)
            .GreaterThan(0)
            .When(request => request.Type == OrderType.DineIn)
            .WithMessage("A dine-in counter order requires a positive table number.");
        RuleFor(request => request.TableNumber)
            .Null()
            .When(request => request.Type != OrderType.DineIn)
            .WithMessage("A table number is valid only for dine-in orders.");
        RuleFor(request => request.ServiceSessionId)
            .Null()
            .When(request => request.Type != OrderType.DineIn)
            .WithMessage("A table service session is valid only for dine-in orders.");
        RuleFor(request => request)
            .Must(request => !request.CustomerUserId.HasValue
                || !request.CustomerId.HasValue
                || request.CustomerUserId == request.CustomerId)
            .WithMessage("CustomerUserId and CustomerId must identify the same customer.");
        RuleFor(request => request.Tip)
            .GreaterThanOrEqualTo(0)
            .When(request => request.Tip.HasValue)
            .WithMessage("Tip cannot be negative.");
        RuleFor(request => request.PaymentState).IsInEnum();
        RuleFor(request => request.PointsToRedeem)
            .GreaterThanOrEqualTo(0)
            .When(request => request.PointsToRedeem.HasValue)
            .WithMessage("PointsToRedeem cannot be negative.");
        RuleFor(request => request.PointsToRedeem)
            .LessThanOrEqualTo(0)
            .When(request => request.PointsToRedeem.HasValue)
            .WithMessage("Points redemption is not supported for staff counter orders.");
        RuleForEach(request => request.Items).ChildRules(item =>
        {
            item.RuleFor(line => line.Quantity).GreaterThan(0);
            item.RuleFor(line => line)
                .Must(line => line.ProductId.HasValue ^ line.MenuId.HasValue)
                .WithMessage("Each item must reference exactly one product or menu.");
        });
    }
}
