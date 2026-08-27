using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("ApiTokens");

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();

        // Base64 SHA-256 is always 44 chars. Unique because two rows with the same hash would
        // make authentication nondeterministic, and because a collision here means a duplicate
        // credential rather than a coincidence.
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.Prefix).HasMaxLength(16).IsRequired();

        // Postgres text[] — the set is always read whole with its row, never queried into.
        builder.Property(t => t.Scopes)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'")
            .IsRequired();
    }
}
