using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OptionSetMaterializationJobConfiguration : IEntityTypeConfiguration<OptionSetMaterializationJob>
{
    public void Configure(EntityTypeBuilder<OptionSetMaterializationJob> builder)
    {
        builder.ToTable("OptionSetMaterializationJobs", table => table.HasCheckConstraint(
            "ck_option_set_materialization_jobs_status",
            "status IN ('queued', 'processing', 'completed', 'partial', 'blocked')"));
        builder.HasKey(job => job.Id);
        builder.Property(job => job.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(job => job.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(job => job.RequestJson).HasColumnType("jsonb").IsRequired();
        builder.Property(job => job.Status).HasMaxLength(24).IsRequired();
        builder.Property(job => job.LastError).HasMaxLength(2000);
        builder.HasOne<OptionSet>().WithMany().HasForeignKey(job => job.OptionSetId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(job => job.Targets).WithOne(target => target.Job)
            .HasForeignKey(target => target.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(job => new { job.OptionSetId, job.IdempotencyKey }).IsUnique();
        builder.HasIndex(job => new { job.Status, job.LeaseExpiresAt, job.CreatedAt });
    }
}
