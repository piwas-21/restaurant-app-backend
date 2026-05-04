using FluentValidation;

namespace RestaurantSystem.Api.Features.Menus.Commands.DeleteMenuBundleCommand;

public class DeleteMenuBundleCommandValidator : AbstractValidator<DeleteMenuBundleCommand>
{
    public DeleteMenuBundleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Menu bundle ID is required");
    }
}
