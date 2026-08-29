using FluentValidation;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInteriorImageCommand;

public class UpdateRestaurantInteriorImageCommandValidator
    : AbstractValidator<UpdateRestaurantInteriorImageCommand>
{
    public UpdateRestaurantInteriorImageCommandValidator()
    {
        // The file itself is checked by ImageUploadRules in the handler, which reports the
        // reason a specific upload was refused. This guards the shape of the command.
        RuleFor(x => x.Image).NotNull().WithMessage("An image file is required");
    }
}
