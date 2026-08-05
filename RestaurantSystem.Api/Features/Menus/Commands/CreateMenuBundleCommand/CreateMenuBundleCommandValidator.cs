using FluentValidation;
using RestaurantSystem.Api.Common.Validation;

namespace RestaurantSystem.Api.Features.Menus.Commands.CreateMenuBundleCommand;

public class CreateMenuBundleCommandValidator : MenuBundleCommandValidatorBase<CreateMenuBundleCommand>
{
    public CreateMenuBundleCommandValidator()
    {
        // CREATE only, deliberately not in the shared base — see ProductContentRule.
        RuleFor(x => x.Content).ValidProductContent(required: true);
    }
}
