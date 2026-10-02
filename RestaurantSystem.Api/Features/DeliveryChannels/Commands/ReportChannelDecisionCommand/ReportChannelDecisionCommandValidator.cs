using FluentValidation;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Commands.ReportChannelDecisionCommand;

public sealed class ReportChannelDecisionCommandValidator : AbstractValidator<ReportChannelDecisionCommand>
{
    public ReportChannelDecisionCommandValidator()
    {
        RuleFor(command => command.DecisionId).NotEmpty();
        RuleFor(command => command.Report.LeaseId).NotEmpty();
        RuleFor(command => command.Report.State).Must(state => state is "Succeeded" or "Failed" or "Unknown");
        RuleFor(command => command.Report.CanonicalState)
            .Must(state => state is "CREATED" or "ACCEPTED" or "DENIED" or "CANCELED" or "FINISHED" or "UNKNOWN");
        RuleFor(command => command.Report.CanonicalHash).Matches("^[a-f0-9]{64}$")
            .When(command => command.Report.CanonicalState != "UNKNOWN");
        RuleFor(command => command.Report.CanonicalHash).Empty()
            .When(command => command.Report.CanonicalState == "UNKNOWN");
        RuleFor(command => command.Report.ObservedAt).NotEmpty();
    }
}
