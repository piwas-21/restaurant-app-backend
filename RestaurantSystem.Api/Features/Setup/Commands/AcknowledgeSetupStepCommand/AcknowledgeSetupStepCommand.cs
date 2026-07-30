using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Setup.Services;

namespace RestaurantSystem.Api.Features.Setup.Commands.AcknowledgeSetupStepCommand;

/// <summary>
/// Mark a setup step done, or un-mark it (SOFRA-ONBOARDING-PLAN O4).
/// </summary>
/// <param name="Key">A step from the <c>SetupSteps</c> vocabulary.</param>
/// <param name="IsDone">
/// True to acknowledge, false to undo. One command both ways: the checklist is
/// resumable, and an owner who ticked the wrong row should not have to live with it.
/// </param>
public record AcknowledgeSetupStepCommand(string Key, bool IsDone) : ICommand<ApiResponse<bool>>;

public class AcknowledgeSetupStepCommandHandler
    : ICommandHandler<AcknowledgeSetupStepCommand, ApiResponse<bool>>
{
    private readonly ISetupChecklistStore _store;
    private readonly ITenantModules _modules;

    public AcknowledgeSetupStepCommandHandler(ISetupChecklistStore store, ITenantModules modules)
    {
        _store = store;
        _modules = modules;
    }

    public async Task<ApiResponse<bool>> Handle(
        AcknowledgeSetupStepCommand command, CancellationToken cancellationToken)
    {
        // Refused, not ignored. `menu` and `staff` are DERIVED — done when the data
        // says so — and accepting a hand-written acknowledgement on one would let an
        // owner tick off a menu they never built. A checklist whose "nothing left to
        // do" can be asserted rather than earned is worth nothing to the person
        // relying on it, which after O4 is a customer with no founder on the call.
        // The honest way out of an unfinished checklist is to dismiss it.
        if (!SetupSteps.IsAcknowledgeable(command.Key))
        {
            throw new BadRequestException(
                $"'{command.Key}' is not a step that can be marked done by hand.");
        }

        // Entitlement is checked on the WRITE, not only when building the response.
        // A stored acknowledgement for a module the tenant has not bought is invisible
        // today and wrong tomorrow: the day they upgrade, the step arrives already
        // ticked and they are never walked through the thing they just paid for.
        if (!SetupSteps.IsEntitledTo(command.Key, _modules))
        {
            throw new BadRequestException(
                $"'{command.Key}' is not part of this restaurant's setup.");
        }

        await _store.ApplyAsync(state =>
        {
            // Set semantics: acknowledging twice is a no-op, and un-acknowledging
            // something that was never acknowledged is too. The UI fires these from a
            // checkbox, so both are ordinary rather than errors.
            var steps = new HashSet<string>(state.AcknowledgedSteps, StringComparer.Ordinal);
            if (command.IsDone) steps.Add(command.Key);
            else steps.Remove(command.Key);
            state.AcknowledgedSteps = [.. steps];
        }, cancellationToken);

        return ApiResponse<bool>.SuccessWithData(command.IsDone);
    }
}
