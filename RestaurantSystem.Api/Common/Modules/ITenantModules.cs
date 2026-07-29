namespace RestaurantSystem.Api.Common.Modules;

/// <summary>
/// Which modules this tenant instance may use at runtime (sofra ADR-010 / S11).
/// Resolved once at startup from <see cref="Settings.ModuleSettings"/>; a change
/// takes effect on the next backend restart, which is exactly when a re-provision
/// rewrites the tenant .env.
/// </summary>
public interface ITenantModules
{
    /// <summary>
    /// False when gating is off for this instance — either the flag is off, or no
    /// module list is configured. While false <see cref="IsEnabled"/> is always true.
    /// </summary>
    bool IsEnforced { get; }

    /// <summary>
    /// The effective module set, in catalog order. When <see cref="IsEnforced"/> is
    /// false this is the whole vocabulary, so a consumer can treat it as a plain
    /// allow-list without also having to reason about the flag.
    /// </summary>
    IReadOnlyList<string> EnabledModules { get; }

    /// <summary>Whether <paramref name="moduleId"/> is available on this instance.</summary>
    bool IsEnabled(string moduleId);
}
