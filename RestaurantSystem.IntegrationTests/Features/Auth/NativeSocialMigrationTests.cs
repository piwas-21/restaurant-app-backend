using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialMigrationTests(DatabaseFixture fixture) : NativeSocialFixture(fixture)
{
    private const string Parent = "20261020031044_AddCustomerOrderCancellationOperations";
    private const string Current = "20261020102803_AddCustomerNativeSocialIdentities";

    [Fact]
    public async Task AdditivePrivateSchemaCanDowngradeAndReapplyWithoutChangingExistingCustomers()
    {
        var customer = await CustomerAsync(confirmed: true);
        await using var context = DatabaseFixture.CreateContext();
        try
        {
            await context.Database.MigrateAsync(Parent);
            Assert.False(await TableExistsAsync("customer_social_identities"));
            Assert.False(await TableExistsAsync("customer_social_challenges"));
            var retained = await context.Users.AsNoTracking().SingleAsync(value => value.Id == customer.Id);
            Assert.Equal(customer.Email, retained.Email);
            Assert.Equal(customer.SecurityStamp, retained.SecurityStamp);
            await context.Database.MigrateAsync();
            Assert.Contains(Current, await context.Database.GetAppliedMigrationsAsync());
            Assert.True(await TableExistsAsync("customer_social_identities"));
            Assert.True(await TableExistsAsync("customer_social_challenges"));
        }
        finally { await context.Database.MigrateAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RollbackCannotDestroyExistingProviderBindingOrLiveChallenge(bool binding)
    {
        if (binding) await CompleteAsync(await ChallengeAsync());
        else await ChallengeAsync();
        await using var context = DatabaseFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.MigrateAsync(Parent));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains(Current, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(binding ? 1 : 0, await context.CustomerSocialIdentities.CountAsync());
        Assert.Single(await context.CustomerSocialChallenges.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RollbackRefusesAnUncommittedWriterThenPreservesItsCommittedAuthority(bool binding)
    {
        var customer = await CustomerAsync(confirmed: true);
        await using var writer = DatabaseFixture.CreateContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        if (binding)
            writer.CustomerSocialIdentities.Add(new RestaurantSystem.Domain.Entities.CustomerSocialIdentity
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                UserId = customer.Id,
                Provider = "google",
                Issuer = Identity.Issuer,
                Subject = Identity.Subject,
                CreatedAt = DateTime.UtcNow
            });
        else
            writer.CustomerSocialChallenges.Add(new RestaurantSystem.Domain.Entities.CustomerSocialChallenge
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                Provider = "google",
                Flow = "login",
                ProofHash = NativeSocialChallengeProof.Hash(NativeSocialChallengeProof.Create()),
                Nonce = NativeSocialChallengeProof.Create(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(3)
            });
        await writer.SaveChangesAsync();
        await using var rollback = DatabaseFixture.CreateContext();
        rollback.Database.SetCommandTimeout(TimeSpan.FromSeconds(8));
        var migration = rollback.Database.MigrateAsync(Parent);
        try
        {
            // Must refuse before the writer commits: a late DROP lock would instead wait here.
            var busy = await Assert.ThrowsAsync<InvalidOperationException>(() => migration.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, Assert.IsType<PostgresException>(busy.InnerException).SqlState);
        }
        finally
        {
            await transaction.CommitAsync();
            try { await migration.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (InvalidOperationException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.LockNotAvailable })
            { }
        }
        var occupied = await Assert.ThrowsAsync<PostgresException>(() => rollback.Database.MigrateAsync(Parent));
        Assert.Equal(PostgresErrorCodes.CheckViolation, occupied.SqlState);
        Assert.Contains(Current, await rollback.Database.GetAppliedMigrationsAsync());
        Assert.Equal(binding ? 1 : 0, await rollback.CustomerSocialIdentities.CountAsync());
        Assert.Equal(binding ? 0 : 1, await rollback.CustomerSocialChallenges.CountAsync());
    }

    private async Task<bool> TableExistsAsync(string table)
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.Database.SqlQuery<bool>($"SELECT to_regclass({table}) IS NOT NULL AS \"Value\"").SingleAsync();
    }
}
