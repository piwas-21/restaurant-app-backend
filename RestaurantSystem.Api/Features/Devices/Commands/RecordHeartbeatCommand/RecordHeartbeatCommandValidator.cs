using FluentValidation;
using RestaurantSystem.Domain.Common.Enums;

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
        RuleFor(x => x.TargetCapabilities)
            .Must(capabilities => capabilities is null
                || capabilities.Count <= Enum.GetValues<DevicePrintTarget>().Length)
            .WithMessage("A heartbeat may report each printer target at most once.");
        RuleFor(x => x.TargetCapabilities)
            .Must(capabilities => capabilities is null
                || capabilities.Select(item => item.Target).Distinct().Count() == capabilities.Count)
            .WithMessage("A heartbeat cannot contain duplicate printer targets.");
        RuleForEach(x => x.TargetCapabilities).ChildRules(capability =>
        {
            capability.RuleFor(item => item.Target).IsInEnum();
            capability.RuleFor(item => item.PrinterName).MaximumLength(120);
        });
        RuleFor(x => x.KitchenRoutingMode).IsInEnum();
    }
}
