using FluentValidation;

namespace RestaurantSystem.Api.Features.Devices.Commands.RecordPrintAcksCommand;

public class RecordPrintAcksCommandValidator : AbstractValidator<RecordPrintAcksCommand>
{
    private const int MaxBatch = 500;

    public RecordPrintAcksCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Acks).NotEmpty();
        RuleFor(x => x.Acks).Must(a => a.Count <= MaxBatch)
            .WithMessage($"At most {MaxBatch} acks per batch.");
        RuleForEach(x => x.Acks).ChildRules(a =>
        {
            a.RuleFor(x => x.OrderId).NotEmpty();
            a.RuleFor(x => x.Copies).GreaterThanOrEqualTo(0);
            a.RuleFor(x => x.FailureReason).MaximumLength(500);
        });
    }
}
