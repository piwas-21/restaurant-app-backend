using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.QuoteStaffCounterOrderCommand;

public sealed class QuoteStaffCounterOrderCommandValidator : AbstractValidator<QuoteStaffCounterOrderCommand>
{
    public QuoteStaffCounterOrderCommandValidator() => Include(new StaffCounterOrderRequestValidator());
}
