using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderItemIngredientConfiguration : IEntityTypeConfiguration<OrderItemIngredient>
{
    public void Configure(EntityTypeBuilder<OrderItemIngredient> builder)
    {
        builder.ToTable("OrderItemIngredients");

        builder.HasKey(oii => oii.Id);

        // 200 mirrors ProductIngredientConfiguration.cs:15-17, the column this one snapshots. A
        // shorter limit here would truncate a name the catalog accepted.
        builder.Property(oii => oii.IngredientName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(oii => oii.Quantity)
            .IsRequired();

        builder.Property(oii => oii.IsRemoved)
            .IsRequired();

        builder.Property(oii => oii.SortOrder)
            .IsRequired();

        builder.HasOne(oii => oii.OrderItem)
            .WithMany(i => i.IngredientSnapshots)
            .HasForeignKey(oii => oii.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // NO foreign key on IngredientId, deliberately. It names a ProductIngredient that a later
        // product save may delete — surviving exactly that is the point of the table — so a
        // constraint here would either block the catalog edit or cascade the history away.
        builder.HasIndex(oii => new { oii.OrderItemId, oii.SortOrder });
    }
}
