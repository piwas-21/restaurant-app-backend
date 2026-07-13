using FluentValidation;

namespace RestaurantSystem.Api.Features.Menus.Commands.UpdateMenuBundleCommand;

public class UpdateMenuBundleCommandValidator : MenuBundleCommandValidatorBase<UpdateMenuBundleCommand>
{
    public UpdateMenuBundleCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Product ID is required");
    }
}
