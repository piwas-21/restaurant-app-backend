using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Settings;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public abstract class CustomerOrderCheckoutJournalFixture(DatabaseFixture database) : CustomerOrderOperationFixture(database)
{
    protected readonly CustomerHostedCheckoutSettings Settings = new()
    {
        Enabled = true,
        LinkOrigin = "https://links.example.test",
        TenantSlug = "fixture"
    };
    protected readonly StripeCommissionSettings Commission = new();
    protected readonly LocalizationSettings Localization = new() { Currency = "CHF" };
    protected readonly JournalClock Clock = new();
    protected string OnlineCapability = "unknown";
    protected bool ProviderReady = true;
    protected bool ChangedCharge;
    protected CustomerOrderRecoveryDto Proof => new() { SchemaVersion = 1, OperationId = Operation, PayloadHash = Payload };

    protected async Task<Guid> SeedAsync(Guid? owner = null)
    {
        await using var context = Database.CreateContext();
        if (owner is { } userId)
        {
            context.Users.Add(new()
            {
                Id = userId,
                UserName = "native-checkout-fixture",
                Email = "native-checkout@example.test",
                FirstName = "Native",
                LastName = "Fixture",
                Role = UserRole.Customer,
                RefreshToken = string.Empty,
                CreatedBy = "native-checkout-fixture"
            });
            await context.SaveChangesAsync();
        }
        var result = await Store(context, owner, owner.HasValue, owner.HasValue ? UserRole.Customer : null)
            .ExecuteAsync(Session, Operation, Payload, async () =>
            {
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    UserId = owner,
                    OrderNumber = Guid.NewGuid().ToString("N")[..20],
                    Type = OrderType.Takeaway,
                    Status = OrderStatus.Pending,
                    PaymentStatus = PaymentStatus.Pending,
                    SubTotal = 12.34m,
                    Total = 12.34m,
                    OrderDate = Clock.GetUtcNow().UtcDateTime,
                    CreatedBy = "native-checkout-fixture"
                };
                order.Items.Add(new()
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductName = "Frozen checkout fixture",
                    Quantity = 1,
                    UnitPrice = 12.34m,
                    ItemTotal = 12.34m,
                    CreatedBy = "native-checkout-fixture"
                });
                order.Payments.Add(new()
                {
                    PaymentMethod = PaymentMethod.OnlinePayment,
                    Amount = 12.34m,
                    Currency = "CHF",
                    Status = PaymentStatus.Processing,
                    PaymentDate = Clock.GetUtcNow().UtcDateTime,
                    CreatedBy = "native-checkout-fixture"
                });
                context.Orders.Add(order);
                await context.SaveChangesAsync();
                var snapshot = OrderBillingSnapshotFactory.Build(order, "CHF", earningEvaluation: null,
                    redemption: null, maximumUnitRows: 1000);
                context.OrderBillingSnapshots.Add(snapshot.Header);
                context.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
                context.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);
                await context.SaveChangesAsync();
                return order.Id;
            }, CancellationToken.None);
        return result.OrderId!.Value;
    }

    protected Task<CustomerOrderCheckoutJournal> FreezeAsync(Guid? owner = null, string? session = null,
        CustomerOrderRecoveryDto? proof = null, IInterceptor[]? interceptors = null) =>
        WithCheckoutAsync((_, journals, _) => journals.FreezeAsync(session ?? Session, proof ?? Proof, CancellationToken.None),
            owner, interceptors);

    protected async Task<T> WithCheckoutAsync<T>(Func<ApplicationDbContext, CustomerOrderCheckoutJournalStore,
        CustomerOrderCheckoutLeaseStore, Task<T>> action, Guid? owner = null, IInterceptor[]? interceptors = null)
    {
        await using var context = Database.CreateContext(interceptors ?? []);
        var guard = new Mock<ICustomerCheckoutGuard>(MockBehavior.Strict);
        guard.Setup(value => value.EnsureOwner(It.IsAny<string>())).Returns(owner);
        var bootstrap = new Mock<IQueryHandler<GetCustomerMobileBootstrapQuery, ApiResponse<CustomerMobileBootstrapDto>>>();
        bootstrap.Setup(value => value.Handle(It.IsAny<GetCustomerMobileBootstrapQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ApiResponse<CustomerMobileBootstrapDto>.SuccessWithData(new(1, Guid.NewGuid(), "CHF", "en",
                "Europe/Zurich", "active", "supported", [], true, new Dictionary<string, bool>(),
                new Dictionary<string, string> { ["ordering"] = "enabled", ["online-payments"] = OnlineCapability })));
        using var services = new ServiceCollection().AddSingleton(bootstrap.Object).BuildServiceProvider();
        var gateway = new Mock<IStripeGateway>(MockBehavior.Strict);
        gateway.SetupGet(value => value.IsConfigured).Returns(ProviderReady);
        gateway.SetupGet(value => value.ConnectedAccountId).Returns("acct_native_fixture");
        var stripe = new StripeSettings { PlatformApiKey = "rk_test_native_fixture" }; // pragma: allowlist secret
        var actualCharges = new CheckoutChargeResolver(Options.Create(Localization), Options.Create(Commission));
        var charge = new Mock<ICheckoutChargeResolver>();
        charge.Setup(value => value.Resolve(It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns((decimal total, string? currency) => actualCharges.Resolve(total + (ChangedCharge ? 0.01m : 0), currency));
        var creation = new CustomerOrderCheckoutCreationPolicy(context, new CustomMediator(services), Options.Create(Settings), charge.Object,
            new OnlinePaymentIntentGuard(context, Mock.Of<RestaurantSystem.Api.Common.Services.Interfaces.ICurrentUserService>()));
        var operations = Store(context, owner, owner.HasValue, owner.HasValue ? UserRole.Customer : null);
        var store = new CustomerOrderCheckoutJournalStore(context, guard.Object,
            operations, gateway.Object, Options.Create(stripe), Options.Create(Settings), creation, Clock);
        var leases = new CustomerOrderCheckoutLeaseStore(context, guard.Object, operations, store, creation, Clock);
        var result = await action(context, store, leases);
        context.Database.CurrentTransaction.Should().BeNull();
        // The strict gateway exposes configuration only. Accessing its provider Client would fail.
        gateway.VerifyGet(value => value.Client, Times.Never);
        return result;
    }

    protected async Task AssertNoCheckoutAsync()
    {
        await using var read = Database.CreateContext();
        (await read.Set<CustomerOrderCheckoutJournal>().CountAsync()).Should().Be(0);
        (await read.OrderCheckoutSessions.CountAsync()).Should().Be(0);
    }

    protected sealed class JournalClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 19, 0, 0, 789, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
