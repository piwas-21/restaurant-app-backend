using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Interfaces;
using RestaurantSystem.Domain.Entities;
using System.Linq.Expressions;
using System.Reflection;

namespace RestaurantSystem.Infrastructure.Persistence
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
    {
        private readonly IAuditIdentityProvider? _auditIdentity;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        /// <summary>
        /// Preferred constructor. The provider is resolved from DI at runtime so audit columns are
        /// stamped with the acting user rather than the literal "System".
        /// </summary>
        /// <remarks>
        /// The parameterless-audit overload above is kept deliberately for callers that construct the
        /// context directly with no container — <c>DatabaseFixture.CreateContext</c> is the one in
        /// this repo. Those saves fall back to "System", which is what they are. (Design-time
        /// <c>dotnet ef</c> resolves through the app host and so gets THIS constructor, not that one.)
        ///
        /// Note the sharp edge the two constructors create: a composition root that registers the
        /// context WITHOUT registering <see cref="IAuditIdentityProvider"/> silently binds the 1-arg
        /// overload and degrades every stamp to "System" with no error. <c>Program.cs</c> registers
        /// both together for that reason.
        /// </remarks>
        [ActivatorUtilitiesConstructor]
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            IAuditIdentityProvider auditIdentity) : base(options)
        {
            _auditIdentity = auditIdentity;
        }

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
        public DbSet<ProductCustomizationGroup> ProductCustomizationGroups { get; set; }
        public DbSet<ProductCustomizationGroupDescription> ProductCustomizationGroupDescriptions { get; set; }
        public DbSet<ProductCustomizationIngredientOption> ProductCustomizationIngredientOptions { get; set; }
        public DbSet<ProductCustomizationProductOption> ProductCustomizationProductOptions { get; set; }
        public DbSet<GlobalIngredient> GlobalIngredients { get; set; }
        public DbSet<GlobalIngredientTranslation> GlobalIngredientTranslations { get; set; }
        public DbSet<GlobalVariation> GlobalVariations { get; set; }
        public DbSet<GlobalVariationTranslation> GlobalVariationTranslations { get; set; }
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
        public DbSet<TableServiceSession> TableServiceSessions { get; set; }
        public DbSet<OrderChange> OrderChanges { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<OrderItemIngredient> OrderItemIngredients { get; set; }
        public DbSet<OrderPayment> OrderPayments { get; set; }
        public DbSet<TableBillPaymentOperation> TableBillPaymentOperations { get; set; }
        public DbSet<OrderOperationalNote> OrderOperationalNotes { get; set; }
        public DbSet<StaffOrderOperation> StaffOrderOperations { get; set; }
        public DbSet<OrderCheckoutSession> OrderCheckoutSessions { get; set; }
        public DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }

        //User-related DbSets
        public DbSet<UserAddress> UserAddresses { get; set; }
        public DbSet<RefreshSession> RefreshSessions { get; set; }

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

        // Floor plan aggregate (FLOOR-PLAN-REVAMP S3)
        public DbSet<FloorPlan> FloorPlans { get; set; }
        public DbSet<FloorPlanItem> FloorPlanItems { get; set; }
        public DbSet<FloorPlanWall> FloorPlanWalls { get; set; }
        public DbSet<FloorPlanOpening> FloorPlanOpenings { get; set; }

        // Tax Configuration
        public DbSet<TaxConfiguration> TaxConfigurations { get; set; }

        // Order Type Configuration
        public DbSet<OrderTypeConfiguration> OrderTypeConfigurations { get; set; }

        // Customer form field configuration (admin-configurable visibility/requiredness)
        public DbSet<CustomerFormFieldConfiguration> CustomerFormFieldConfigurations { get; set; }

        // Working Hours
        public DbSet<WorkingHours> WorkingHours { get; set; }

        // One row per SERVING WINDOW inside a day: a lunch/dinner split is two of these.
        public DbSet<WorkingHoursShift> WorkingHoursShifts { get; set; }

        // Restaurant identity + contact details (singleton row)
        public DbSet<RestaurantInfo> RestaurantInfo { get; set; }
        public DbSet<RestaurantPhoneNumber> RestaurantPhoneNumbers { get; set; }
        public DbSet<RestaurantLandingContent> RestaurantLandingContents { get; set; }

        // First-run setup checklist (singleton row, created lazily on first write)
        public DbSet<SetupChecklistState> SetupChecklistState { get; set; }

        // User Groups & Discounts
        public DbSet<UserGroup> UserGroups { get; set; }
        public DbSet<GroupMembership> GroupMemberships { get; set; }
        public DbSet<GroupDiscount> GroupDiscounts { get; set; }

        // Fleet observability
        public DbSet<PrinterDevice> PrinterDevices { get; set; }
        public DbSet<DeviceOrderReceipt> DeviceOrderReceipts { get; set; }
        public DbSet<DeviceEvent> DeviceEvents { get; set; }

        // Outbound mail claims — the idempotency anchor for every mail the server sends itself
        // (EMAIL-SPEC-TENANT-APP GAP-11/GAP-12).
        public DbSet<OutboundEmail> OutboundEmails { get; set; }

        // Machine credentials — scoped API tokens (docs/plans/API-TOKENS-PLAN.md)
        public DbSet<ApiToken> ApiTokens { get; set; }

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

        /// <summary>
        /// The override that actually matters. Every save in this codebase is asynchronous — there
        /// are no <c>SaveChanges()</c> callers outside the override above — so until this existed,
        /// <see cref="ApplyAuditInformation"/> never ran in production at all.
        /// </summary>
        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            ApplyAuditInformation();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        /// <summary>
        /// Fills in audit columns a handler did not set, and turns a <c>Remove</c> of a
        /// soft-deletable entity into a soft delete.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Backfill only — never overwrite.</b> Handlers stamp these columns themselves at ~125
        /// callsites via <c>ICurrentUserService.GetAuditIdentifier()</c>, and some deliberately
        /// write a non-user identity (<c>BasketCleanupService</c> writes its own name). Clobbering
        /// those would destroy real audit identity, so a value the caller set in this unit of work
        /// is left exactly as it is; this only supplies what is missing.
        /// </para>
        /// <para>
        /// "Did the caller set it?" is asked of EF, not of the value: <c>IsModified</c> on the
        /// property is true only when this unit of work changed it. Comparing against null would
        /// get <c>Modified</c> wrong, because a row loaded from the database already carries the
        /// previous save's UpdatedBy and would then never refresh.
        /// </para>
        /// </remarks>
        private void ApplyAuditInformation()
        {
            ChangeTracker.DetectChanges();

            var now = DateTime.UtcNow;
            var userId = _auditIdentity?.GetAuditIdentifier() ?? "System";
            var orderEntries = ChangeTracker.Entries<Order>()
                .Where(entry => entry.Entity.Id != Guid.Empty)
                .ToDictionary(entry => entry.Entity.Id);
            var orderItemEntries = ChangeTracker.Entries<OrderItem>()
                .Where(entry => entry.Entity.Id != Guid.Empty)
                .ToDictionary(entry => entry.Entity.Id, entry => entry.Entity.OrderId);
            AddPersistedOrderItemOwners(orderItemEntries);
            AddPersistedOrderOwners(orderEntries, orderItemEntries);

            // An order detail is an aggregate read: a tender, status-history row, note, line, or
            // delivery snapshot changes what that read means even when the Order row itself was
            // not otherwise assigned. Touch the tracked owner before stamping so the one UPDATE
            // carries both the audit columns and the optimistic-concurrency version.
            TouchOrderOwners(orderEntries, orderItemEntries);

            foreach (var entry in ChangeTracker.Entries<IAuditable>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        StampCreation(entry, now, userId);
                        break;

                    case EntityState.Modified:
                        StampUpdate(entry, now, userId);
                        break;
                }
            }

            foreach (var entry in ChangeTracker.Entries<ISoftDelete>())
            {
                if (entry.State == EntityState.Deleted)
                {
                    ConvertDeleteToSoftDelete(entry, now, userId);
                }
            }
        }

        /// <summary>
        /// Promotes a tracked order to Modified when a directly-owned detail row changed. Set-based
        /// updates cannot reach this hook and must bump the order explicitly at their call site.
        /// </summary>
        private void AddPersistedOrderItemOwners(Dictionary<Guid, Guid> orderItemEntries)
        {
            var missingItemIds = ChangeTracker.Entries<OrderItemIngredient>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => entry.Entity.OrderItemId)
                .Where(itemId => !orderItemEntries.ContainsKey(itemId))
                .Distinct()
                .ToArray();

            if (missingItemIds.Length == 0)
            {
                return;
            }

            foreach (var owner in OrderItems
                .Where(item => missingItemIds.Contains(item.Id))
                .Select(item => new { item.Id, item.OrderId })
                .ToList())
            {
                orderItemEntries[owner.Id] = owner.OrderId;
            }
        }

        private void AddPersistedOrderOwners(
            Dictionary<Guid, EntityEntry<Order>> orderEntries,
            Dictionary<Guid, Guid> orderItemEntries)
        {
            var changedOrderIds = ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => entry.Entity switch
                {
                    OrderPayment payment => payment.OrderId,
                    OrderOperationalNote note => note.OrderId,
                    OrderStatusHistory history => history.OrderId,
                    OrderCheckoutSession session => session.OrderId,
                    FidelityPointsTransaction points => points.OrderId ?? Guid.Empty,
                    OrderAddress address => address.OrderId,
                    OrderItem item => item.OrderId,
                    OrderItemIngredient ingredient when orderItemEntries.TryGetValue(
                        ingredient.OrderItemId, out var ownerId) => ownerId,
                    _ => OwnedOrderId(entry),
                })
                .Where(orderId => orderId != Guid.Empty && !orderEntries.ContainsKey(orderId))
                .Distinct()
                .ToArray();

            foreach (var order in Orders
                .Where(value => changedOrderIds.Contains(value.Id)).ToList())
            {
                orderEntries[order.Id] = Entry(order);
            }
        }

        private static Guid OwnedOrderId(EntityEntry entry)
        {
            var ownership = entry.Metadata.FindOwnership();
            if (ownership?.PrincipalEntityType.ClrType != typeof(Order))
            {
                return Guid.Empty;
            }

            var key = ownership.Properties.SingleOrDefault();
            return key is not null && entry.Property(key.Name).CurrentValue is Guid orderId
                ? orderId
                : Guid.Empty;
        }

        private void TouchOrderOwners(
            IReadOnlyDictionary<Guid, EntityEntry<Order>> orderEntries,
            Dictionary<Guid, Guid> orderItemEntries)
        {
            TouchDirectOrderOwners<OrderPayment>(payment => payment.OrderId, orderEntries);
            TouchDirectOrderOwners<OrderOperationalNote>(note => note.OrderId, orderEntries);
            TouchDirectOrderOwners<OrderStatusHistory>(history => history.OrderId, orderEntries);
            TouchDirectOrderOwners<OrderCheckoutSession>(session => session.OrderId, orderEntries);
            TouchDirectOrderOwners<FidelityPointsTransaction>(points => points.OrderId, orderEntries);
            TouchDirectOrderOwners<OrderAddress>(address => address.OrderId, orderEntries);
            TouchDirectOrderOwners<OrderItem>(item => item.OrderId, orderEntries);
            TouchOrderItemIngredientOwners(orderEntries, orderItemEntries);
            TouchOwnedOrderOwners(orderEntries);
        }

        private void TouchDirectOrderOwners<TEntity>(
            Func<TEntity, Guid?> orderIdSelector,
            IReadOnlyDictionary<Guid, EntityEntry<Order>> orderEntries)
            where TEntity : class
        {
            foreach (var entry in ChangeTracker.Entries<TEntity>().ToArray())
            {
                if (orderIdSelector(entry.Entity) is Guid orderId)
                {
                    TouchOrderOwner(orderId, orderEntries, entry.State);
                }
            }
        }

        // Ingredient snapshots point at their line rather than directly at the order. The line
        // map is built from the same tracked graph, so editing a snapshot cannot silently leave
        // the detail version unchanged.
        private void TouchOrderItemIngredientOwners(
            IReadOnlyDictionary<Guid, EntityEntry<Order>> orderEntries,
            Dictionary<Guid, Guid> orderItemEntries)
        {
            foreach (var entry in ChangeTracker.Entries<OrderItemIngredient>().ToArray())
            {
                if (orderItemEntries.TryGetValue(entry.Entity.OrderItemId, out var orderId))
                {
                    TouchOrderOwner(orderId, orderEntries, entry.State);
                }
            }
        }

        // OrderFocus is an optional owned row stored in the orders table and therefore has no
        // OrderId CLR property. Its ownership key is the owner's key, which lets this cover a
        // direct focus edit as well as the command that replaces the owned value.
        private void TouchOwnedOrderOwners(
            IReadOnlyDictionary<Guid, EntityEntry<Order>> orderEntries)
        {
            foreach (var entry in ChangeTracker.Entries()
                .Where(entry => entry.Metadata.FindOwnership()?.PrincipalEntityType.ClrType == typeof(Order))
                .ToArray())
            {
                var ownership = entry.Metadata.FindOwnership();
                var key = ownership?.Properties.SingleOrDefault();
                var value = key is null ? null : entry.Property(key.Name).CurrentValue;
                if (value is Guid orderId)
                {
                    TouchOrderOwner(orderId, orderEntries, entry.State);
                }
            }
        }

        private static void TouchOrderOwner(
            Guid orderId,
            IReadOnlyDictionary<Guid, EntityEntry<Order>> orderEntries,
            EntityState childState)
        {
            if (orderId == Guid.Empty || childState is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) ||
                !orderEntries.TryGetValue(orderId, out var owner) || owner.State != EntityState.Unchanged)
            {
                return;
            }

            owner.State = EntityState.Modified;
        }

        /// <remarks>
        /// <see cref="PropertyEntry.IsModified"/> is false for every property of an Added entry —
        /// even ones the caller assigned in the initializer — so it cannot distinguish a deliberate
        /// value from an unset one. The value itself can: <c>CreatedAt</c> is only ever
        /// <c>default(DateTime)</c> when nobody assigned it.
        ///
        /// The <c>CreatedAt</c> stamp is belt-and-braces: <c>ConfigureDefaultValues</c> gives every
        /// <see cref="IAuditable"/> a <c>CURRENT_TIMESTAMP</c> store default, so an unset value gets
        /// filled by Postgres anyway. It is kept so the value is on the in-memory entity without a
        /// round-trip — and it is why no test pins it: through the save path the two are
        /// indistinguishable, and a test asserting "CreatedAt is populated afterwards" passes with
        /// this line deleted. <c>CreatedBy</c> has no such default and IS pinned.
        /// </remarks>
        private static void StampCreation(EntityEntry<IAuditable> entry, DateTime now, string userId)
        {
            if (entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }

            if (string.IsNullOrEmpty(entry.Entity.CreatedBy))
            {
                entry.Entity.CreatedBy = userId;
            }

            // Historical rows receive 1 from the migration, while new objects should be safe even
            // when a caller bypasses the CLR initializer.
            if (entry.Entity is Order order && order.Version <= 0)
            {
                order.Version = 1;
            }
        }

        private static void StampUpdate(EntityEntry<IAuditable> entry, DateTime now, string userId)
        {
            if (entry.Entity is Order order)
            {
                // Version is application-managed so every tracked order mutation, not only the POS
                // handlers, invalidates a detail snapshot. EF includes the original value in the
                // UPDATE predicate because OrderConfiguration marks it as a concurrency token.
                order.Version++;
            }

            if (!IsSetInThisSave(entry, nameof(IAuditable.UpdatedAt)))
            {
                entry.Entity.UpdatedAt = now;
            }

            if (!IsSetInThisSave(entry, nameof(IAuditable.UpdatedBy)))
            {
                entry.Entity.UpdatedBy = userId;
            }
        }

        /// <summary>
        /// Re-targets a <c>Remove()</c> at the <c>IsDeleted</c> flag. Read back through the global
        /// query filter the row simply disappears, which is why this is invisible to callers.
        /// </summary>
        private static void ConvertDeleteToSoftDelete(EntityEntry<ISoftDelete> entry, DateTime now, string userId)
        {
            // Read the caller's intent BEFORE re-targeting the delete, and NOT via IsModified: on a
            // Deleted entry IsModified is false for every property regardless of what the caller
            // assigned, so an IsModified guard here is constant-false and silently overwrites.
            // Comparing current against original still works on a Deleted entry — both value sets
            // survive. (Asking after the flip is no better: the flip marks EVERY property modified,
            // including ones nobody touched.)
            var deletedAtWasSet = WasAssignedOnDeletedEntry(entry, nameof(ISoftDelete.DeletedAt));
            var deletedByWasSet = WasAssignedOnDeletedEntry(entry, nameof(ISoftDelete.DeletedBy));
            var updatedAtWasSet = WasAssignedOnDeletedEntry(entry, nameof(IAuditable.UpdatedAt));
            var updatedByWasSet = WasAssignedOnDeletedEntry(entry, nameof(IAuditable.UpdatedBy));

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;

            if (entry.Entity is Order order)
            {
                order.Version++;
            }

            if (!deletedAtWasSet)
            {
                entry.Entity.DeletedAt = now;
            }

            if (!deletedByWasSet)
            {
                entry.Entity.DeletedBy = userId;
            }

            // A soft delete IS a modification, so stamp the update columns too. The IAuditable loop
            // cannot do it: it runs first, and at that point this entry is still Deleted, which its
            // switch has no case for. Without this the row is rewritten carrying a stale
            // UpdatedAt/UpdatedBy — worse than leaving them alone, because the flip marks those
            // columns modified and they get written back as if they were current.
            //
            // Guarded like every other column here. An unconditional stamp would be the same
            // clobbering bug as an IsModified guard on a Deleted entry, just relocated.
            if (entry.Entity is not IAuditable auditable)
            {
                return;
            }

            if (!updatedAtWasSet)
            {
                auditable.UpdatedAt = now;
            }

            if (!updatedByWasSet)
            {
                auditable.UpdatedBy = userId;
            }
        }

        /// <summary>
        /// True when this unit of work assigned <paramref name="propertyName"/>, i.e. the caller
        /// meant that value and it must not be overwritten.
        /// </summary>
        private static bool IsSetInThisSave(EntityEntry entry, string propertyName)
        {
            var property = entry.Metadata.FindProperty(propertyName);

            // An entity that does not map the column at all cannot have been set through it.
            return property is not null && entry.Property(propertyName).IsModified;
        }

        /// <summary>
        /// The <see cref="EntityState.Deleted"/> equivalent of <see cref="IsSetInThisSave"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="PropertyEntry.IsModified"/> is false for every property of a Deleted entry, so
        /// it cannot answer this question — but the original and current value sets are both still
        /// populated, and a difference between them means the caller assigned it.
        ///
        /// That implication runs one way only. An entity <c>Remove</c>d while DETACHED has its
        /// originals snapshotted from the stub at attach time, so original == current even for a
        /// column the caller deliberately set, and the assignment is missed. Every <c>Remove</c> in
        /// this codebase operates on a tracked, loaded entity, so that is a latent trap rather than
        /// a live one — but it is the case to check first if a deliberate DeletedBy ever comes back
        /// as the ambient identity.
        /// </remarks>
        private static bool WasAssignedOnDeletedEntry(EntityEntry entry, string propertyName)
        {
            var property = entry.Metadata.FindProperty(propertyName);
            if (property is null)
            {
                return false;
            }

            var propertyEntry = entry.Property(propertyName);
            return !Equals(propertyEntry.OriginalValue, propertyEntry.CurrentValue);
        }

    }
}
