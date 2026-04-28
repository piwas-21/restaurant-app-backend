using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RestaurantSystem.Domain.Common.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Configurations;
using System.Linq.Expressions;
using System.Reflection;

namespace RestaurantSystem.Infrastructure.Persistence
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        // Product-related DbSets
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<ProductCategory> ProductCategories { get; set; }
        public DbSet<ProductVariation> ProductVariations { get; set; }
        public DbSet<ProductSideItem> ProductSideItems { get; set; }
        public DbSet<ProductVariationDescription> ProductVariationDescriptions { get; set; }
        public DbSet<ProductDescription> ProductDescriptions { get; set; }
        public DbSet<ProductIngredient> ProductIngredients { get; set; }
        public DbSet<ProductIngredientDescription> ProductIngredientDescriptions { get; set; }
        public DbSet<GlobalIngredient> GlobalIngredients { get; set; }
        public DbSet<GlobalIngredientTranslation> GlobalIngredientTranslations { get; set; }
        public DbSet<Menu> Menus { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<MenuDefinition> MenuDefinitions { get; set; }
        public DbSet<MenuSection> MenuSections { get; set; }
        public DbSet<MenuSectionItem> MenuSectionItems { get; set; }

        // Basket-related

        public DbSet<Basket> Baskets { get; set; }
        public DbSet<BasketItem> BasketItems { get; set; }

        // Order-related DbSets
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<OrderPayment> OrderPayments { get; set; }
        public DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }

        //User-related DbSets
        public DbSet<UserAddress> UserAddresses { get; set; }

        public DbSet<OrderAddress> OrderAddresses { get; set; }

        // Fidelity Points & Discounts
        public DbSet<FidelityPointsTransaction> FidelityPointsTransactions { get; set; }
        public DbSet<FidelityPointBalance> FidelityPointBalances { get; set; }
        public DbSet<PointEarningRule> PointEarningRules { get; set; }
        public DbSet<CustomerDiscountRule> CustomerDiscountRules { get; set; }

        // Reservation-related DbSets
        public DbSet<Table> Tables { get; set; }
        public DbSet<Reservation> Reservations { get; set; }
        public DbSet<TableReservation> TableReservations { get; set; }

        // Tax Configuration
        public DbSet<TaxConfiguration> TaxConfigurations { get; set; }

        // Order Type Configuration
        public DbSet<OrderTypeConfiguration> OrderTypeConfigurations { get; set; }

        // Working Hours
        public DbSet<WorkingHours> WorkingHours { get; set; }

        // Restaurant identity + contact details (singleton row)
        public DbSet<RestaurantInfo> RestaurantInfo { get; set; }
        public DbSet<RestaurantPhoneNumber> RestaurantPhoneNumbers { get; set; }

        // User Groups & Discounts
        public DbSet<UserGroup> UserGroups { get; set; }
        public DbSet<GroupMembership> GroupMemberships { get; set; }
        public DbSet<GroupDiscount> GroupDiscounts { get; set; }

        // Data Protection Keys
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;


        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            ConfigurePostgreSQL(builder);

            builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

            ConfigureSoftDeleteFilter(builder);

            ConfigureDefaultValues(builder);

        }

        private void ConfigurePostgreSQL(ModelBuilder builder)
        {
            // Convert all table and column names to lowercase
            foreach (var entity in builder.Model.GetEntityTypes())
            {
                // Convert table names to lowercase and use snake_case
                entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

                // Convert column names to lowercase and use snake_case
                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(ToSnakeCase(property.GetColumnName()));
                }

                // Convert primary keys to lowercase and use snake_case
                foreach (var key in entity.GetKeys())
                {
                    key.SetName(ToSnakeCase(key.GetName()!));
                }

                // Convert foreign keys to lowercase and use snake_case
                foreach (var key in entity.GetForeignKeys())
                {
                    key.SetConstraintName(ToSnakeCase(key.GetConstraintName()!));
                }

                // Convert indexes to lowercase and use snake_case
                foreach (var index in entity.GetIndexes())
                {
                    index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
                }
            }
        }

        private string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            // Already has underscores, assume it's already snake_case
            if (input.Contains('_'))
            {
                return input.ToLower();
            }

            var startUnderscores = Enumerable
                .Range(0, input.Length)
                .Where(i => i == 0 ? input[i] == '_' : input[i] == '_' && input[i - 1] == '_')
                .Count();

            var snakeCase = string.Concat(input.Select((x, i) => i > 0 && char.IsUpper(x) ? $"_{x}" : x.ToString()))
                .ToLower();

            return string.Concat(Enumerable.Repeat("_", startUnderscores)) + snakeCase;
        }

        private void ConfigureSoftDeleteFilter(ModelBuilder builder)
        {
            // Apply global query filter for soft delete entities
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                // Check if the entity implements ISoftDelete
                if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType) &&
                    !typeof(IExcludeFromGlobalFilter).IsAssignableFrom(entityType.ClrType))
                {
                    var parameter = Expression.Parameter(entityType.ClrType, "e");
                    var property = Expression.Property(parameter, "IsDeleted");
                    var falseConstant = Expression.Constant(false);
                    var expression = Expression.Equal(property, falseConstant);
                    var lambda = Expression.Lambda(expression, parameter);

                    builder.Entity(entityType.ClrType).HasQueryFilter(lambda);
                }
            }
        }

        private void ConfigureDefaultValues(ModelBuilder builder)
        {
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                // Set default values for IAuditable entities
                if (typeof(IAuditable).IsAssignableFrom(entityType.ClrType))
                {
                    builder.Entity(entityType.ClrType)
                        .Property("CreatedAt")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");
                }

                // Set default values for ISoftDelete entities
                if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
                {
                    builder.Entity(entityType.ClrType)
                        .Property("IsDeleted")
                        .HasDefaultValue(false);
                }

                // Set default for Guid primary keys
                var keyProperty = entityType.FindProperty("Id");
                if (keyProperty != null && keyProperty.ClrType == typeof(Guid))
                {
                    keyProperty.ValueGenerated = ValueGenerated.OnAdd;
                    keyProperty.SetDefaultValueSql("gen_random_uuid()");
                }
            }
        }

        public override int SaveChanges()
        {
            ApplyAuditInformation();
            return base.SaveChanges();
        }

        private void ApplyAuditInformation()
        {
            var now = DateTime.UtcNow;

            // Get the current user ID if available
            var userId = "System"; // Default if no user context is available

            foreach (var entry in ChangeTracker.Entries<IAuditable>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAt = now;
                        entry.Entity.CreatedBy = userId;
                        break;

                    case EntityState.Modified:
                        entry.Entity.UpdatedAt = now;
                        entry.Entity.UpdatedBy = userId;
                        break;
                }
            }

            // Handle soft deletion
            foreach (var entry in ChangeTracker.Entries<ISoftDelete>())
            {
                if (entry.State == EntityState.Deleted)
                {
                    // Change state from deleted to modified and set IsDeleted flag
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = now;
                    entry.Entity.DeletedBy = userId;
                }
            }
        }

    }
}
