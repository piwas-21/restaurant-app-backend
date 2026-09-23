using FluentValidation;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Orders.Commands.QuoteStaffCounterOrderCommand;

public sealed class QuoteStaffCounterOrderCommandValidator : AbstractValidator<QuoteStaffCounterOrderCommand>
{
    public QuoteStaffCounterOrderCommandValidator(IOptions<FidelitySettings> fidelitySettings) =>
        Include(new StaffCounterOrderRequestValidator(fidelitySettings));
}
