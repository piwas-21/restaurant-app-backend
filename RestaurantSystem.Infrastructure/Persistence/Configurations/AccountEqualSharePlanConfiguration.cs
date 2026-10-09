using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class AccountEqualSharePlanConfiguration : IEntityTypeConfiguration<AccountEqualSharePlan>
{
    public void Configure(EntityTypeBuilder<AccountEqualSharePlan> builder)
    {
        builder.ToTable("account_equal_share_plans", table => table.HasCheckConstraint(
            "ck_account_equal_share_plan_shape", "total_minor > 0 AND share_count > 0 AND account_revision > 0"));
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.ScopeJson).HasColumnType("jsonb").IsRequired();
        builder.Property(value => value.CustomAmountsJson).HasColumnType("jsonb");
        builder.Property(value => value.ActorId).IsRequired(false);
        builder.Property(value => value.ActorKind).HasConversion<string>().HasMaxLength(20).IsRequired(false);
        builder.HasIndex(value => value.OperationId).IsUnique();
        builder.HasIndex(value => value.ServiceSessionId);
        builder.HasOne(value => value.ServiceSession).WithMany().HasForeignKey(value => value.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
