using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.CompleteAllTableOrdersCommand;

public class CompleteAllTableOrdersCommandValidator : AbstractValidator<CompleteAllTableOrdersCommand>
{
    public CompleteAllTableOrdersCommandValidator()
    {
        RuleFor(x => x.TableNumber).NotEmpty().WithMessage("Table number is required");
    }
}
