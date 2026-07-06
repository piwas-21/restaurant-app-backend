using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Infrastructure.Persistence.Seeders
{
    public static class RestaurantInfoSeeder
    {
        /// <summary>
        /// Replaces the migration-seeded tenant-1 (RUMI) identity in the
        /// <c>RestaurantInfo</c> singleton with values from configuration
        /// (issue #120). Applies only while the row is pristine
        /// (<c>UpdatedAt == null</c>, i.e. never modified since the
        /// AddRestaurantInfo migration inserted it) — the seeder stamps
        /// <c>UpdatedAt</c> itself on success (the audit hook only covers the
        /// sync SaveChanges path), so admin-owned data is never touched, a
        /// second boot is a no-op, and a first boot that failed mid-way
        /// self-corrects on retry.
        /// </summary>
        public static async Task SeedAsync(ApplicationDbContext dbContext, ILogger logger, RestaurantInfoSeedSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Email))
            {
                logger.LogInformation("RestaurantInfo seed skipped: RestaurantInfoSeed.Name/Email not configured — migration defaults stay in place.");
                return;
            }

            var info = await dbContext.RestaurantInfo
                .Include(r => r.PhoneNumbers)
                .SingleOrDefaultAsync()
                ?? throw new InvalidOperationException(
                    "RestaurantInfoSeed is configured but the RestaurantInfo singleton is missing — the AddRestaurantInfo migration should have created it. Failing startup so a mis-provisioned tenant is caught immediately.");

            if (info.UpdatedAt != null)
            {
                logger.LogInformation("RestaurantInfo seed skipped: the singleton has been modified since the migration seed (UpdatedAt set) — it is admin-owned data now.");
                return;
            }

            info.Name = settings.Name.Trim();
            info.Email = settings.Email.Trim();
            // City is optional; defensive null-coalesce in case a caller
            // constructs the settings with a null (config binding never does).
            info.City = (settings.City ?? string.Empty).Trim();

            // The migration seeds tenant-1 (RUMI) identity; none of it belongs
            // on a fresh tenant install. Fields the registry doesn't provide
            // start blank and are completed by the tenant admin in General
            // Settings — same for the migration-seeded phone number below (a
            // pristine singleton means no admin has managed phones yet).
            info.AddressLine1 = string.Empty;
            info.AddressLine2 = null;
            info.PostalCode = string.Empty;
            info.Country = string.Empty;
            info.Latitude = null;
            info.Longitude = null;
            info.Website = null;

            // Explicit stamp: this is what flips the row to "not pristine".
            // ApplyAuditInformation only runs on the sync SaveChanges path,
            // so without this the seeder would re-run on every boot (and
            // could wipe phones an admin added before ever editing the row).
            info.UpdatedAt = DateTime.UtcNow;
            info.UpdatedBy = "RestaurantInfoSeeder";

            dbContext.RestaurantPhoneNumbers.RemoveRange(info.PhoneNumbers);

            await dbContext.SaveChangesAsync();
            logger.LogInformation("RestaurantInfo seeded for tenant '{Name}': migration defaults replaced from RestaurantInfoSeed configuration.", info.Name);
        }
    }
}
