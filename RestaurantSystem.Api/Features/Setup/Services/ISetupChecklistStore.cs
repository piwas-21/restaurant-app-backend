using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Setup.Services;

/// <summary>
/// Reads and writes the singleton <see cref="SetupChecklistState"/> row
/// (SOFRA-ONBOARDING-PLAN O4).
/// </summary>
/// <remarks>
/// Exists so the lazy-create-and-save dance lives in one place. Both writers need it,
/// and the concurrency handling is the sort of thing that gets implemented correctly
/// once and then subtly differently the second time.
/// </remarks>
public interface ISetupChecklistStore
{
    /// <summary>The row, or null when nothing has ever been written.</summary>
    Task<SetupChecklistState?> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Apply <paramref name="mutate"/> to the row, creating it if it does not exist,
    /// and save.
    /// </summary>
    /// <remarks>
    /// Survives the concurrent-first-write race: if another request inserts the
    /// singleton between this one's read and its save, the duplicate-key violation is
    /// caught, the winner's row is re-read, and the mutation is re-applied on top.
    /// </remarks>
    Task ApplyAsync(Action<SetupChecklistState> mutate, CancellationToken cancellationToken);
}
