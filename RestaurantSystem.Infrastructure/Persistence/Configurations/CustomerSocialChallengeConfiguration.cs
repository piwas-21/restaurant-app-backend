using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerSocialChallengeConfiguration : IEntityTypeConfiguration<CustomerSocialChallenge>
{
    public void Configure(EntityTypeBuilder<CustomerSocialChallenge> builder)
    {
        builder.ToTable("customer_social_challenges", table =>
        {
            table.HasCheckConstraint("ck_customer_social_challenge_flow",
                "(flow = 'login' AND user_id IS NULL AND security_stamp_hash IS NULL) OR "
                + "(flow = 'link' AND user_id IS NOT NULL AND security_stamp_hash IS NOT NULL)");
            table.HasCheckConstraint("ck_customer_social_challenge_lifetime", "expires_at > created_at");
        });
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.Id).ValueGeneratedNever();
        builder.Property(challenge => challenge.Provider).HasMaxLength(16).IsRequired();
        builder.Property(challenge => challenge.Flow).HasMaxLength(8).IsRequired();
        builder.Property(challenge => challenge.ProofHash).HasMaxLength(64).IsRequired();
        builder.Property(challenge => challenge.Nonce).HasMaxLength(128).IsRequired();
        builder.Property(challenge => challenge.SecurityStampHash).HasMaxLength(64);
        builder.HasIndex(challenge => challenge.ExpiresAt);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(challenge => challenge.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
