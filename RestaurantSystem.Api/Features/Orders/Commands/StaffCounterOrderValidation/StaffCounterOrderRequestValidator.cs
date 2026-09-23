using FluentValidation;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
namespace RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;

public sealed class StaffCounterOrderRequestValidator : AbstractValidator<StaffCounterOrderRequest>
{
    public StaffCounterOrderRequestValidator()
    {
        RuleFor(request => request.Type).IsInEnum(); RuleFor(request => request.Items).NotEmpty().WithMessage("A counter order must contain at least one item.");
        RuleFor(request => request.TableNumber)
            .GreaterThan(0)
            .When(request => request.Type == OrderType.DineIn && !request.TableId.HasValue)
            .WithMessage("A dine-in counter order requires a positive table number.");
        RuleFor(request => request)
            .Must(request => request.ServiceSessionId.HasValue)
            .When(request => request.Type == OrderType.DineIn)
            .WithMessage("A dine-in staff order requires an open table service session.");
        RuleFor(request => request.TableNumber)
            .Null()
            .When(request => request.Type != OrderType.DineIn)
            .WithMessage("A table number is valid only for dine-in orders.");
        RuleFor(request => request.TableId)
            .Null()
            .When(request => request.Type != OrderType.DineIn)
            .WithMessage("A table id is valid only for dine-in orders.");
        RuleFor(request => request.ServiceSessionId)
            .Null()
            .When(request => request.Type != OrderType.DineIn)
            .WithMessage("A table service session is valid only for dine-in orders.");
        this.ValidateDeliveryAddress();
        this.ValidateNotesLength();
        this.ValidateStaffCustomer();
        RuleFor(request => request.Tip)
            .GreaterThanOrEqualTo(0)
            .When(request => request.Tip.HasValue)
            .WithMessage("Tip cannot be negative.");
        RuleFor(request => request.PaymentState).IsInEnum();
        RuleForEach(request => request.Items).ChildRules(item =>
        {
            item.RuleFor(line => line.Quantity).GreaterThan(0);
            item.RuleFor(line => line)
                .Must(line => line.ProductId.HasValue ^ line.MenuId.HasValue)
                .WithMessage("Each item must reference exactly one product or menu.");
        });
    }
}
