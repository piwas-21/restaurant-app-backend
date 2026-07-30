namespace RestaurantSystem.Api.Features.Setup.Dtos;

/// <summary>Body of <c>PUT /api/admin/setup-checklist/steps/{key}</c>.</summary>
/// <param name="IsDone">Desired state: true acknowledges the step, false undoes it.</param>
/// <remarks>
/// The desired state rather than a toggle, so the request is idempotent — a checkbox
/// re-sent after a dropped connection lands on the same answer instead of flipping back.
/// </remarks>
public record SetStepDoneRequest(bool IsDone);

/// <summary>Body of <c>PUT /api/admin/setup-checklist/dismissed</c>.</summary>
/// <param name="IsDismissed">True hides the checklist, false restores it.</param>
public record SetDismissedRequest(bool IsDismissed);
