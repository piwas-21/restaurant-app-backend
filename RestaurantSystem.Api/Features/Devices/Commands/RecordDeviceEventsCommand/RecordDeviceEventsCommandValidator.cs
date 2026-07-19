using FluentValidation;

namespace RestaurantSystem.Api.Features.Devices.Commands.RecordDeviceEventsCommand;

public class RecordDeviceEventsCommandValidator : AbstractValidator<RecordDeviceEventsCommand>
{
    private const int MaxBatch = 500;

    public RecordDeviceEventsCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Events).NotEmpty();
        RuleFor(x => x.Events).Must(e => e.Count <= MaxBatch)
            .WithMessage($"At most {MaxBatch} events per batch.");
        RuleForEach(x => x.Events).ChildRules(e =>
        {
            e.RuleFor(x => x.ClientEventId).NotEmpty().MaximumLength(64);
            // Level persists as a string; reject out-of-range values so they can't store as "0".
            e.RuleFor(x => x.Level).IsInEnum();
            e.RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
            e.RuleFor(x => x.Code).MaximumLength(80);
            e.RuleFor(x => x.Context).MaximumLength(4000);
        });
    }
}
