using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OptionSetMatchDecisionConfiguration : IEntityTypeConfiguration<OptionSetMatchDecision>
{
    public void Configure(EntityTypeBuilder<OptionSetMatchDecision> builder)
    {
        builder.ToTable("OptionSetMatchDecisions");
        builder.HasKey(decision => decision.Id);
        builder.Property(decision => decision.NormalizedName)
            .HasMaxLength(OptionSetSchemaLimits.NormalizedNameLength).IsRequired();
        builder.Property(decision => decision.CandidateType).HasMaxLength(40).IsRequired();
        builder.Property(decision => decision.Alias).HasMaxLength(OptionSetSchemaLimits.AliasLength);
        builder.HasIndex(decision => new { decision.NormalizedName, decision.CandidateType, decision.CandidateId }).IsUnique();
    }
}
