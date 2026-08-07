using FluentValidation;
using RestaurantSystem.Api.Common.Validation;

namespace RestaurantSystem.Api.Features.Menus.Commands.UpdateMenuBundleCommand;

public class UpdateMenuBundleCommandValidator : MenuBundleCommandValidatorBase<UpdateMenuBundleCommand>
{
    public UpdateMenuBundleCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Product ID is required");

        // #306. Stated here rather than in the shared base, and the same goes for the create
        // validator: putting it in the base AND passing `required: true` in the derived class
        // registers the rule TWICE (AbstractValidator appends, it does not dedupe), so every
        // malformed bundle answered with each message duplicated. No `required` here — this
        // handler coalesces, where an absent map means "no translation changes" (#190).
        RuleFor(x => x.Content).ValidProductContent(required: false);
    }
}
