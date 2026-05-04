using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ReleaseTableCommand;

public class ReleaseTableCommandValidator : AbstractValidator<ReleaseTableCommand>
{
    public ReleaseTableCommandValidator()
    {
        RuleFor(x => x.TableNumber).NotEmpty().WithMessage("Table number is required");
    }
}
