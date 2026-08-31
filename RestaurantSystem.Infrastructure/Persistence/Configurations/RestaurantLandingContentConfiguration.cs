using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class RestaurantLandingContentConfiguration : IEntityTypeConfiguration<RestaurantLandingContent>
{
    public void Configure(EntityTypeBuilder<RestaurantLandingContent> builder)
    {
        builder.ToTable("RestaurantLandingContents");

        builder.Property(content => content.LanguageCode)
            .IsRequired()
            .HasMaxLength(LanguageCode.MaxLength);
        builder.Property(content => content.HeroEyebrow).HasMaxLength(100);
        builder.Property(content => content.WelcomeTitle).HasMaxLength(200);
        builder.Property(content => content.WelcomeBody).HasMaxLength(4_000);
        builder.Property(content => content.StoryTitle).HasMaxLength(200);
        builder.Property(content => content.StoryBody).HasMaxLength(4_000);

        builder.HasIndex(content => new { content.RestaurantInfoId, content.LanguageCode }).IsUnique();
        builder.HasOne(content => content.RestaurantInfo)
            .WithMany(info => info.LandingContents)
            .HasForeignKey(content => content.RestaurantInfoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
