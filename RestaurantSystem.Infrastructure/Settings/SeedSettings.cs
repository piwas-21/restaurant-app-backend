namespace RestaurantSystem.Infrastructure.Settings;

/// <summary>
/// Startup-seed configuration (bound from the "SeedSettings" section; env vars
/// override JSON, so per-tenant provisioning injects SeedSettings__AdminEmail /
/// SeedSettings__AdminPassword — sofra ADR-003). Lives in Infrastructure because
/// the seeder consuming it does; the Api layer binds it in Program.cs.
/// </summary>
public class SeedSettings
{
    /// <summary>
    /// Email of the admin account created on a fresh database. When empty,
    /// admin seeding is skipped (roles are always seeded) — the safe default
    /// for environments where the admin already exists or is created manually.
    /// </summary>
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>
    /// Initial password for the seeded admin. Never a source literal (issue
    /// #116); comes from app-secrets.json / environment per deployment.
    /// </summary>
    public string AdminPassword { get; set; } = string.Empty;

    public string AdminFirstName { get; set; } = "Admin";

    public string AdminLastName { get; set; } = "User";

    /// <summary>
    /// Seed the E2E menu fixtures — a mixed-kitchen bundle the printer-app suite needs to assert
    /// kitchen routing (issue #238). <b>Defaults to false and must stay false in production:</b>
    /// this inserts a visible, orderable product, so a tenant would find "E2E Menu Deal" on their
    /// menu. Enabled per-environment on staging/demo via
    /// <c>SeedSettings__SeedE2EMenuFixtures=true</c>.
    /// </summary>
    public bool SeedE2EMenuFixtures { get; set; }
}
