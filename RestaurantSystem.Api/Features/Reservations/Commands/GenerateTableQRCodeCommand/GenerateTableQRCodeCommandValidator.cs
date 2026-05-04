using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.GenerateTableQRCodeCommand;

public class GenerateTableQRCodeCommandValidator : AbstractValidator<GenerateTableQRCodeCommand>
{
    public GenerateTableQRCodeCommandValidator()
    {
        RuleFor(x => x.TableId).NotEmpty().WithMessage("TableId is required");
    }
}
