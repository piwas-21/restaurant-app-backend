using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        //RuleFor(x => x.Type)
        //    .IsInEnum()
        //    .WithMessage("Invalid order type");

        //RuleFor(x => x.TableNumber)
        //    .NotNull()
        //    .GreaterThan(0)
        //    .When(x => x.Type == OrderType.DineIn)
        //    .WithMessage("Table number is required for dine-in orders");

        //RuleFor(x => x.DeliveryAddress)
        //    .NotEmpty()
        //    .When(x => x.Type == OrderType.Delivery)
        //    .WithMessage("Delivery address is required for delivery orders");

        //RuleFor(x => x.Items)
        //    .NotEmpty()
        //    .WithMessage("Order must have at least one item");

        //RuleForEach(x => x.Items)
        //    .ChildRules(item =>
        //    {
        //        item.RuleFor(i => i.ProductId)
        //            .NotEmpty()
        //            .WithMessage("Product ID is required");

        //        item.RuleFor(i => i.Quantity)
        //            .GreaterThan(0)
        //            .WithMessage("Quantity must be greater than 0");
        //    });

        //RuleFor(x => x.Payments)
        //    .NotEmpty()
        //    .WithMessage("Order must have at least one payment method");

        //RuleForEach(x => x.Payments)
        //    .ChildRules(payment =>
        //    {
        //        payment.RuleFor(p => p.PaymentMethod)
        //            .IsInEnum()
        //            .WithMessage("Invalid payment method");

        //        payment.RuleFor(p => p.Amount)
        //            .GreaterThan(0)
        //            .WithMessage("Payment amount must be greater than 0");
        //    });

        //RuleFor(x => x.Priority)
        //    .InclusiveBetween(1, 5)
        //    .When(x => x.IsFocusOrder && x.Priority.HasValue)
        //    .WithMessage("Priority must be between 1 and 5");

        //RuleFor(x => x.FocusReason)
        //    .NotEmpty()
        //    .When(x => x.IsFocusOrder)
        //    .WithMessage("Focus reason is required for focus orders");
    }
}
