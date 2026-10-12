using FluentValidation;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateTipPreferencesCommand;

public sealed class UpdateTipPreferencesCommandValidator : AbstractValidator<UpdateTipPreferencesCommand>
{
    private const int MaximumPresets = 6;
    private const int MaximumFixedMinor = 100_000;
    public UpdateTipPreferencesCommandValidator()
    {
        RuleFor(command => command.Mode).Must(mode => mode is "percentage" or "fixed");
        RuleFor(command => command.Values).Cascade(CascadeMode.Stop).NotNull()
            .Must(values => values.Length is > 0 and <= MaximumPresets)
            .Must(values => values.Distinct().Count() == values.Length);
        RuleForEach(command => command.Values).GreaterThan(0)
            .LessThanOrEqualTo(command => command.Mode == "percentage" ? 100 : MaximumFixedMinor);
        RuleFor(command => command.Currency).NotEmpty().Matches("^[A-Z]{3}$")
            .When(command => command.Mode == "fixed");
    }
}
