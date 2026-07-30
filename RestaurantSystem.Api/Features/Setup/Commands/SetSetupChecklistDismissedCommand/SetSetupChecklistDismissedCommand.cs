using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Setup.Services;

namespace RestaurantSystem.Api.Features.Setup.Commands.SetSetupChecklistDismissedCommand;

/// <summary>
/// Hide or restore the first-run checklist (SOFRA-ONBOARDING-PLAN O4).
/// </summary>
/// <param name="IsDismissed">True to hide it, false to bring it back.</param>
/// <remarks>
/// Both directions on one command because the checklist is required to be
/// <i>resumable</i>: an owner who hides it mid-menu on a busy Friday has to be able to
/// pick it back up, and a dismissal that could not be undone would make the guidance a
/// single-use thing most owners lose on their first day.
/// <para>
/// Dismissal never marks anything done. It hides the list; the steps keep their real
/// state underneath, so restoring it resumes rather than restarts.
/// </para>
/// </remarks>
public record SetSetupChecklistDismissedCommand(bool IsDismissed) : ICommand<ApiResponse<bool>>;

public class SetSetupChecklistDismissedCommandHandler
    : ICommandHandler<SetSetupChecklistDismissedCommand, ApiResponse<bool>>
{
    private readonly ISetupChecklistStore _store;

    public SetSetupChecklistDismissedCommandHandler(ISetupChecklistStore store)
    {
        _store = store;
    }

    public async Task<ApiResponse<bool>> Handle(
        SetSetupChecklistDismissedCommand command, CancellationToken cancellationToken)
    {
        // Restoring a checklist nobody ever dismissed is already the state — do not
        // create a row just to record "not dismissed".
        if (!command.IsDismissed && await _store.GetAsync(cancellationToken) is null)
        {
            return ApiResponse<bool>.SuccessWithData(false);
        }

        // `DateTime.UtcNow` matches every sibling handler under Features/Settings; this
        // repo has no TimeProvider registration and one field is not the place to add one.
        await _store.ApplyAsync(
            state => state.DismissedAt = command.IsDismissed ? DateTime.UtcNow : null,
            cancellationToken);

        return ApiResponse<bool>.SuccessWithData(command.IsDismissed);
    }
}
