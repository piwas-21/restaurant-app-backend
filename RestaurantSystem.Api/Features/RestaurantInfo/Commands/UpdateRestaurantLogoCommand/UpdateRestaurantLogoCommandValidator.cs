using FluentValidation;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantLogoCommand;

public class UpdateRestaurantLogoCommandValidator : AbstractValidator<UpdateRestaurantLogoCommand>
{
    public UpdateRestaurantLogoCommandValidator()
    {
        // The file itself is checked by ImageUploadRules in the handler, which reports the
        // reason a specific upload was refused. This guards the shape of the command.
        RuleFor(x => x.Logo).NotNull().WithMessage("A logo file is required");
        RuleFor(x => x.Variant).IsInEnum().WithMessage("Unknown logo variant");
    }
}
