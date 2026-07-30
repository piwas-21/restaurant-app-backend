using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class SetupChecklistStateConfiguration : IEntityTypeConfiguration<SetupChecklistState>
{
    public void Configure(EntityTypeBuilder<SetupChecklistState> builder)
    {
        builder.ToTable("SetupChecklistState");

        // Stored as a Postgres text[] (Npgsql's native mapping for a primitive
        // collection). Nothing queries INTO this array — the singleton row is always
        // read whole — so an array column beats a child table for a handful of short
        // keys, and beats a delimited string for not needing a parser.
        builder.Property(s => s.AcknowledgedSteps)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'")
            .IsRequired();
    }
}
