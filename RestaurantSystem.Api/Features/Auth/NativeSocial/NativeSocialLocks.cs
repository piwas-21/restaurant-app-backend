using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public static class NativeSocialLocks
{
    public static Task PoolAsync(ApplicationDbContext context, Guid tenant, CancellationToken cancellationToken) =>
        AcquireAsync(context, "native-social-pool\n" + tenant.ToString("D"), cancellationToken);
    public static Task IdentityAsync(ApplicationDbContext context, Guid tenant, NativeSocialIdentity identity,
        CancellationToken cancellationToken) => AcquireAsync(context,
        "native-social-identity\n" + tenant.ToString("D") + "\n" + identity.Provider + "\n" + identity.Issuer + "\n" + identity.Subject,
        cancellationToken);
    private static async Task AcquireAsync(ApplicationDbContext context, string value, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Native social locks require an active transaction.");
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }
}
