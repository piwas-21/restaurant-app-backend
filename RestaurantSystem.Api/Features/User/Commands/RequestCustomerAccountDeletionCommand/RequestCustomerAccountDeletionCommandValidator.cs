using FluentValidation;

namespace RestaurantSystem.Api.Features.User.Commands.RequestCustomerAccountDeletionCommand;

public sealed class RequestCustomerAccountDeletionCommandValidator : AbstractValidator<RequestCustomerAccountDeletionCommand>
{
    public RequestCustomerAccountDeletionCommandValidator() => RuleFor(command => command.SchemaVersion).Equal(1);
}
