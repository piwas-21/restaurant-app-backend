using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OptionSetMaterializationJobTargetConfiguration
    : IEntityTypeConfiguration<OptionSetMaterializationJobTarget>
{
    public void Configure(EntityTypeBuilder<OptionSetMaterializationJobTarget> builder)
    {
        builder.ToTable("OptionSetMaterializationJobTargets", table => table.HasCheckConstraint(
            "ck_option_set_materialization_job_targets_status",
            "status IN ('pending', 'applied', 'unchanged', 'conflict', 'failed')"));
        builder.HasKey(target => target.Id);
        builder.Property(target => target.TargetKey).HasMaxLength(120).IsRequired();
        builder.Property(target => target.RequestJson).HasColumnType("jsonb").IsRequired();
        builder.Property(target => target.ResultJson).HasColumnType("jsonb");
        builder.Property(target => target.Status).HasMaxLength(24).IsRequired();
        builder.Property(target => target.ErrorCode).HasMaxLength(120);
        builder.Property(target => target.ErrorMessage).HasMaxLength(OptionSetMaterializationLimits.PersistedErrorMaxLength);
        builder.HasIndex(target => new { target.JobId, target.Sequence }).IsUnique();
        builder.HasIndex(target => new { target.JobId, target.Status, target.Sequence });
        builder.HasIndex(target => new { target.JobId, target.TargetKey }).IsUnique();
    }
}
