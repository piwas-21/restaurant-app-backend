using FluentValidation;

namespace RestaurantSystem.Api.Features.Auth.Commands.RegisterNativeCustomerCommand;

public sealed class RegisterNativeCustomerCommandValidator : AbstractValidator<RegisterNativeCustomerCommand>
{
    public RegisterNativeCustomerCommandValidator()
    {
        RuleFor(command => command.SchemaVersion).Equal(1);
        RuleFor(command => command.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(command => command.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(command => command.LastName).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(1000);
        RuleFor(command => command.ConfirmPassword).NotEmpty().MaximumLength(1000);
    }
}
