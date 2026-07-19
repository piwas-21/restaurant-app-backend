using FluentValidation;

namespace RestaurantSystem.Api.Features.Devices.Commands.RecordHeartbeatCommand;

public class RecordHeartbeatCommandValidator : AbstractValidator<RecordHeartbeatCommand>
{
    public RecordHeartbeatCommandValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("The X-Device-Id header is required.")
            .MaximumLength(64).WithMessage("Device id must be 64 characters or fewer.");
        RuleFor(x => x.Label).MaximumLength(120);
        RuleFor(x => x.TenantSlug).MaximumLength(80);
        RuleFor(x => x.Platform).MaximumLength(40);
        RuleFor(x => x.AppVersion).MaximumLength(40);
        RuleFor(x => x.ApiBaseUrl).MaximumLength(300);
        RuleFor(x => x.KitchenPrinter).MaximumLength(120);
        RuleFor(x => x.CashierPrinter).MaximumLength(120);
    }
}
