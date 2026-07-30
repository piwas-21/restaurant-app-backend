namespace RestaurantSystem.Api.Features.Setup.Dtos;

/// <summary>
/// One checklist step as the admin UI sees it.
/// </summary>
/// <param name="Key">Stable step id; the frontend's i18n key stem and route lookup.</param>
/// <param name="ModuleId">Owning module, or null when every tenant needs the step.</param>
/// <param name="IsDerived">
/// True when <paramref name="IsDone"/> was OBSERVED from real data. The UI must not
/// offer a "mark as done" control for these — the API refuses it.
/// </param>
/// <param name="IsDone">Complete: observed for a derived step, acknowledged otherwise.</param>
public record SetupStepDto(string Key, string? ModuleId, bool IsDerived, bool IsDone);

/// <summary>
/// The tenant's first-run setup state (SOFRA-ONBOARDING-PLAN O4).
/// </summary>
/// <param name="IsDismissed">The checklist is hidden. Reversible — it is resumable.</param>
/// <param name="DoneCount">How many of <paramref name="Steps"/> are complete.</param>
/// <param name="Steps">
/// Only the steps this tenant's modules entitle them to, in the order to work through.
/// </param>
public record SetupChecklistDto(
    bool IsDismissed,
    int DoneCount,
    IReadOnlyList<SetupStepDto> Steps);
