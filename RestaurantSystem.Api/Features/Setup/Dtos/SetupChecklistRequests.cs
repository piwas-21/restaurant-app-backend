namespace RestaurantSystem.Api.Features.Setup.Dtos;

/// <summary>Body of <c>PUT /api/admin/setup-checklist/steps/{key}</c>.</summary>
/// <remarks>
/// The desired state rather than a toggle, so the request is idempotent — a checkbox
/// re-sent after a dropped connection lands on the same answer instead of flipping back.
/// <para>
/// <c>required</c>, not a positional <c>bool</c> (Sonar S6964). A non-nullable value type
/// is bound to <c>default</c> when the field is absent, so <c>PUT {}</c> would mean
/// <c>isDone: false</c> — silently UN-acknowledging a step the owner had ticked, on a
/// request that said nothing about it. <c>required</c> makes System.Text.Json reject the
/// body instead, which is a 400 the caller can see.
/// </para>
/// </remarks>
public record SetStepDoneRequest
{
    /// <summary>Desired state: true acknowledges the step, false undoes it.</summary>
    public required bool IsDone { get; init; }
}

/// <summary>Body of <c>PUT /api/admin/setup-checklist/dismissed</c>.</summary>
/// <remarks><c>required</c> for the same reason as <see cref="SetStepDoneRequest"/>: an
/// absent field would otherwise restore a checklist nobody asked to restore.</remarks>
public record SetDismissedRequest
{
    /// <summary>True hides the checklist, false restores it.</summary>
    public required bool IsDismissed { get; init; }
}
