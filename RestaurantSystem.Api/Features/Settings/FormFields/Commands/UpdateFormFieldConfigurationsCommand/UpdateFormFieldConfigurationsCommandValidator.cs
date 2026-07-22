using FluentValidation;

namespace RestaurantSystem.Api.Features.Settings.FormFields.Commands.UpdateFormFieldConfigurationsCommand;

public class UpdateFormFieldConfigurationsCommandValidator
    : AbstractValidator<UpdateFormFieldConfigurationsCommand>
{
    public UpdateFormFieldConfigurationsCommandValidator()
    {
        RuleFor(x => x.Fields).NotNull().WithMessage("Fields are required");
        RuleForEach(x => x.Fields).ChildRules(field =>
        {
            field.RuleFor(f => f.FormKey).NotEmpty().WithMessage("FormKey is required");
            field.RuleFor(f => f.FieldKey).NotEmpty().WithMessage("FieldKey is required");
        }).When(x => x.Fields != null);
    }
}
