using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using System.Text.Json;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations
{
    sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
    {
        public void Configure(EntityTypeBuilder<ApplicationUser> builder)
        {
            builder.ToTable("Users");

            builder.Property(u => u.Role)
            .HasConversion(new EnumToStringConverter<UserRole>());

            builder.HasIndex(u => u.Email)
                  .IsUnique();

            builder.HasIndex(u => u.NormalizedEmail)
                .IsUnique();

            builder.HasIndex(u => u.NormalizedUserName)
                .IsUnique();

            var jsonDictionaryConverter = new ValueConverter<Dictionary<string, string>, string>(
                  v => JsonSerializer.Serialize(v, new JsonSerializerOptions { WriteIndented = false }),
                  v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, new JsonSerializerOptions()) ?? new Dictionary<string, string>()
              );

            builder.Property<Dictionary<string, string>>("Metadata")
                .HasColumnName("metadata")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb")
                .HasConversion(jsonDictionaryConverter);

            builder.HasIndex(u => new { u.NormalizedUserName, u.IsDeleted })
              .IsUnique()
              .HasFilter("\"is_deleted\" = false");

            builder.HasIndex(u => new { u.NormalizedEmail, u.IsDeleted })
                .IsUnique()
                .HasFilter("\"is_deleted\" = false");

            builder.Property(r => r.OrderLimitAmount)
            .HasColumnType("decimal(10,2)");

            builder.Property(r => r.DiscountPercentage)
                .HasColumnType("decimal(5,2)");

            // The whitelist is enforced HERE, not only on the write path: a handler that assigns
            // a raw header would otherwise store a value S5 feeds to CultureInfo, or blow the
            // 10-char column and turn a guest's order into a 500 (Npgsql 22001) inside
            // SaveChangesAsync. EF does not invoke a reference-type converter for null, so the
            // "no preference recorded" signal survives.
            builder.Property(r => r.PreferredLanguage)
                .HasMaxLength(LanguageCode.MaxLength)
                .HasConversion(value => LanguageCode.Normalize(value), stored => stored);
        }
    }
}
