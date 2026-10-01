using FluentValidation;
using RestaurantSystem.Api.Features.DeliveryChannels.Validation;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Commands.CreateExternalOrderCommand;

public sealed class CreateExternalOrderCommandValidator : AbstractValidator<CreateExternalOrderCommand>
{
    public CreateExternalOrderCommandValidator()
    {
        RuleFor(command => command.Request).NotNull().SetValidator(new ExternalOrderRequestValidator());
    }
}
