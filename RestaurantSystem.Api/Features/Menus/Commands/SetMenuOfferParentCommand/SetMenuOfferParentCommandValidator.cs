using FluentValidation;

namespace RestaurantSystem.Api.Features.Menus.Commands.SetMenuOfferParentCommand;

public sealed class SetMenuOfferParentCommandValidator : AbstractValidator<SetMenuOfferParentCommand>
{
    public SetMenuOfferParentCommandValidator()
    {
        RuleFor(command => command.MenuProductId)
            .NotEmpty()
            .WithMessage("Menu bundle ID is required");

        RuleFor(command => command.ParentOfferVariationId)
            .Null()
            .When(command => !command.ParentOfferProductId.HasValue)
            .WithMessage("A parent offer product is required when a variation is supplied");
    }
}
