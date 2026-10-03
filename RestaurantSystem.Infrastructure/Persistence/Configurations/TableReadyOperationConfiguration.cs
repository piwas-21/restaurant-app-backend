using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TableReadyOperationConfiguration : IEntityTypeConfiguration<TableReadyOperation>
{
    public void Configure(EntityTypeBuilder<TableReadyOperation> builder)
    {
        builder.ToTable("table_ready_operations");
        builder.Property(operation => operation.OperationId).IsRequired();
        builder.Property(operation => operation.ActorUserId).IsRequired();
        builder.Property(operation => operation.ActorRole).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.ExpectedReadinessVersion).IsRequired();
        builder.Property(operation => operation.Succeeded).IsRequired();
        builder.Property(operation => operation.OutcomeErrorCode).HasMaxLength(80);
        builder.Property(operation => operation.OutcomeState).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(operation => operation.OutcomeReadinessVersion).IsRequired();
        builder.Property(operation => operation.RecordedAt).IsRequired();
        builder.HasIndex(operation => new { operation.TableId, operation.OperationId }).IsUnique();
        builder.HasIndex(operation => operation.TableId);
        builder.HasOne(operation => operation.Table)
            .WithMany(table => table.ReadyOperations)
            .HasForeignKey(operation => operation.TableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
