using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// Pins the audit/soft-delete interceptor to ASYNCHRONOUS saves.
/// </summary>
/// <remarks>
/// <para>
/// Before this, <c>ApplicationDbContext</c> overrode only the synchronous <c>SaveChanges</c>. Every
/// save in the codebase is asynchronous — there are no synchronous callers at all — so the
/// interceptor never ran in production: no audit column was auto-stamped and a <c>Remove</c> of a
/// soft-deletable entity was a HARD delete.
/// </para>
/// <para>
/// The identity is stubbed with a sentinel rather than left to the ambient user. That is
/// deliberate: outside an HTTP request <c>ICurrentUserService</c> resolves to "System", which is
/// also what the context falls back to when nothing is injected — so a test asserting "System"
/// would pass just as happily against a context that never received a provider at all, and could
/// not tell a working fix from a silently unwired one. A sentinel can only appear if EF selected
/// the constructor that takes <see cref="IAuditIdentityProvider"/>.
/// </para>
/// </remarks>
[Collection("Database Lane 3")]
public class AuditInterceptorTests : IntegrationTestBase
{
    private const string Sentinel = "audit-provider-sentinel";

    public AuditInterceptorTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private sealed class StubAuditIdentity : IAuditIdentityProvider
    {
        public string GetAuditIdentifier() => Sentinel;
    }

    /// <summary>
    /// A scope whose <see cref="IAuditIdentityProvider"/> is the sentinel stub, resolved through
    /// the application's own container so EF's constructor selection is genuinely exercised.
    /// </summary>
    private ApplicationDbContext CreateContextWithStubbedIdentity(out IServiceScope scope)
    {
        var factory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IAuditIdentityProvider>(_ => new StubAuditIdentity())));

        scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    private static GlobalIngredient NewIngredient(string createdBy) => new()
    {
        Id = Guid.NewGuid(),
        DefaultName = $"audit-test-{Guid.NewGuid():N}",
        CreatedBy = createdBy
    };

    [Fact]
    public async Task SaveChangesAsync_StampsUpdatedBy_WhenTheCallerDidNotSetIt()
    {
        // The regression test for the whole defect: this assertion failed before SaveChangesAsync
        // was overridden, because the interceptor simply never ran on an async save.
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient("creator");
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            ingredient.IsActive = false;
            await context.SaveChangesAsync();

            Assert.Equal(Sentinel, ingredient.UpdatedBy);
            Assert.NotNull(ingredient.UpdatedAt);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotOverwrite_AnUpdatedByTheCallerSet()
    {
        // Handlers stamp this themselves at ~125 callsites, and some write a deliberate non-user
        // identity ("BasketCleanupService"). Clobbering those would destroy real audit identity.
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient("creator");
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            ingredient.IsActive = false;
            ingredient.UpdatedBy = "BasketCleanupService";
            await context.SaveChangesAsync();

            Assert.Equal("BasketCleanupService", ingredient.UpdatedBy);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotOverwrite_ACreatedByTheCallerSet()
    {
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient("deliberate-creator");
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            Assert.Equal("deliberate-creator", ingredient.CreatedBy);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_RefreshesUpdatedBy_OnEverySubsequentSave()
    {
        // Guards the obvious wrong way to write "backfill only": treating a non-null UpdatedBy as
        // "the caller set it" would freeze the FIRST save's value forever, because a tracked
        // entity already carries the previous save's stamp. The check has to ask EF whether THIS
        // unit of work modified the property.
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient("creator");
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            ingredient.IsActive = false;
            ingredient.UpdatedBy = "first-writer";
            await context.SaveChangesAsync();

            ingredient.IsActive = true;
            await context.SaveChangesAsync();

            Assert.Equal(Sentinel, ingredient.UpdatedBy);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_TurnsRemoveIntoASoftDelete()
    {
        // The other half of the defect: Remove() + an async save was a HARD delete, so the row left
        // no audit trail and the soft-delete strategy the entity declares was never applied.
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient("creator");
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            context.GlobalIngredients.Remove(ingredient);
            await context.SaveChangesAsync();

            var row = await context.GlobalIngredients
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(g => g.Id == ingredient.Id);

            Assert.NotNull(row);
            Assert.True(row.IsDeleted);
            Assert.Equal(Sentinel, row.DeletedBy);
            Assert.NotNull(row.DeletedAt);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotOverwrite_ADeletedByTheCallerSet()
    {
        // The Deleted-state counterpart of the "does not overwrite" guards above, and the one that
        // is easiest to get wrong: PropertyEntry.IsModified is false for EVERY property of a Deleted
        // entry, so the obvious guard is constant-false and clobbers silently. This fails if the
        // implementation goes back to asking IsModified.
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient("creator");
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            ingredient.DeletedBy = "AccountCleanupService";
            context.GlobalIngredients.Remove(ingredient);
            await context.SaveChangesAsync();

            var row = await context.GlobalIngredients
                .IgnoreQueryFilters()
                .SingleAsync(g => g.Id == ingredient.Id);

            Assert.True(row.IsDeleted);
            Assert.Equal("AccountCleanupService", row.DeletedBy);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotOverwrite_ADeletedAtTheCallerSet()
    {
        // The DeletedBy half was covered and the DeletedAt half was not: mutating its guard to
        // `true` failed nothing. Same regression, other column.
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient("creator");
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            var deliberate = new DateTime(2021, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            ingredient.DeletedAt = deliberate;
            context.GlobalIngredients.Remove(ingredient);
            await context.SaveChangesAsync();

            var row = await context.GlobalIngredients
                .IgnoreQueryFilters()
                .SingleAsync(g => g.Id == ingredient.Id);

            Assert.Equal(deliberate, row.DeletedAt);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotOverwrite_AnUpdatedByTheCallerSet_OnASoftDelete()
    {
        // The soft-delete branch stamps UpdatedAt/UpdatedBy as well, and an unconditional stamp
        // there would be the same clobbering bug relocated: the "never overwrite" contract must
        // hold on the Deleted path too, not just on a plain Modified save.
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient("creator");
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            ingredient.UpdatedBy = "BasketCleanupService";
            context.GlobalIngredients.Remove(ingredient);
            await context.SaveChangesAsync();

            var row = await context.GlobalIngredients
                .IgnoreQueryFilters()
                .SingleAsync(g => g.Id == ingredient.Id);

            Assert.Equal("BasketCleanupService", row.UpdatedBy);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_AlsoRefreshesTheUpdateColumns_OnASoftDelete()
    {
        // A soft delete is a row modification, but the IAuditable loop runs while the entry is still
        // Deleted and its switch has no case for that. Without an explicit stamp the row is rewritten
        // carrying a stale UpdatedBy, which reads as though someone else made the change.
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient("creator");
            ingredient.UpdatedBy = "someone-earlier";
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            context.GlobalIngredients.Remove(ingredient);
            await context.SaveChangesAsync();

            var row = await context.GlobalIngredients
                .IgnoreQueryFilters()
                .SingleAsync(g => g.Id == ingredient.Id);

            Assert.Equal(Sentinel, row.UpdatedBy);
            Assert.NotNull(row.UpdatedAt);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotOverwrite_ACreatedAtTheCallerSet()
    {
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var deliberate = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var ingredient = NewIngredient("creator");
            ingredient.CreatedAt = deliberate;

            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            Assert.Equal(deliberate, ingredient.CreatedAt);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_StampsCreatedBy_WhenTheCallerLeftItEmpty()
    {
        // `required string CreatedBy` does not make this dead: EF materialisation and `null!` both
        // bypass `required`, and an empty string satisfies it outright.
        var context = CreateContextWithStubbedIdentity(out var scope);
        using (scope)
        {
            var ingredient = NewIngredient(string.Empty);
            context.GlobalIngredients.Add(ingredient);
            await context.SaveChangesAsync();

            Assert.Equal(Sentinel, ingredient.CreatedBy);
        }
    }
}
