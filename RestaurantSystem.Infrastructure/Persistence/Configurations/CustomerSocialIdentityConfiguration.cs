using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerSocialIdentityConfiguration : IEntityTypeConfiguration<CustomerSocialIdentity>
{
    public void Configure(EntityTypeBuilder<CustomerSocialIdentity> builder)
    {
        builder.ToTable("customer_social_identities");
        builder.HasKey(identity => identity.Id);
        builder.Property(identity => identity.Id).ValueGeneratedNever();
        builder.Property(identity => identity.Provider).HasMaxLength(16).IsRequired();
        builder.Property(identity => identity.Issuer).HasMaxLength(256).IsRequired();
        builder.Property(identity => identity.Subject).HasMaxLength(255).IsRequired();
        builder.HasIndex(identity => new { identity.TenantId, identity.Provider, identity.Issuer, identity.Subject }).IsUnique();
        builder.HasIndex(identity => new { identity.TenantId, identity.UserId, identity.Provider }).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(identity => identity.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
