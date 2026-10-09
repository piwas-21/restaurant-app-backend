using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class KitchenBoardWorkCompletionConfiguration
    : IEntityTypeConfiguration<KitchenBoardWorkCompletion>
{
    public void Configure(EntityTypeBuilder<KitchenBoardWorkCompletion> builder)
    {
        builder.ToTable("kitchen_board_work_completions", table => table.HasCheckConstraint(
            "ck_kitchen_board_work_completion_values",
            "acknowledged_order_version > 0 AND (account_revision IS NULL OR account_revision > 0)"));
        builder.Property(work => work.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(work => work.Sequence).HasDefaultValueSql(
            "next_kitchen_board_completion_sequence()").ValueGeneratedOnAdd();
        builder.HasIndex(work => new { work.OrderId, work.WorkItemId, work.Kind }).IsUnique();
        builder.HasIndex(work => work.Sequence).IsUnique();
        builder.HasOne(work => work.Order)
            .WithMany()
            .HasForeignKey(work => work.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
