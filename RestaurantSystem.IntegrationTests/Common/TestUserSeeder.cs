using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// Helper for seeding ApplicationUser rows that other entities can FK to.
///
/// FidelityPoints / discount / order entities have FK constraints to AspNetUsers.
/// Tests that mint a fresh <c>Guid.NewGuid()</c> as <c>UserId</c> need to insert
/// a corresponding user row first; otherwise <c>SaveChangesAsync</c> fails with
/// 23503 (foreign key violation).
///
/// The seeded user is minimal (Customer role, soft-delete clean) and reuses
/// <c>userId</c> as its primary key plus deterministic field values, so tests
/// remain isolated from each other via <c>DatabaseFixture.ResetDatabaseAsync</c>.
/// </summary>
public static class TestUserSeeder
{
    public static async Task SeedUserAsync(ApplicationDbContext context, Guid userId, string? email = null)
    {
        if (await context.Users.FindAsync(userId) is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = email ?? $"test-{userId:N}@example.com",
            Email = email ?? $"test-{userId:N}@example.com",
            FirstName = "Test",
            LastName = "User",
            Role = UserRole.Customer,
            CreatedBy = "TestUserSeeder",
            CreatedAt = DateTime.UtcNow,
            RefreshToken = string.Empty,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
    }
}
