using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class AccountPaymentAccountReaderTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly Guid _actorId = Guid.NewGuid();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Projection_conserves_historical_captured_reserved_and_available_minor_values_without_private_fields()
    {
        var identity = await SeedPartiallyPaidAccount();
        await using var context = fixture.CreateContext();
        var account = await Reader(context).GetAsync(identity.SessionId, CancellationToken.None);

        account.ServiceSessionId.Should().Be(identity.SessionId);
        account.Status.Should().Be(TableServiceSessionStatus.Open);
        account.AccountRevision.Should().Be(5);
        account.Currency.Should().Be("CHF");
        account.OutstandingMinor.Should().Be(1000);
        account.ReservedMinor.Should().Be(300);
        account.AvailableMinor.Should().Be(700);
        account.CapturedAccountPaymentMinor.Should().Be(300);
        account.OutstandingMinor.Should().Be(account.ReservedMinor + account.AvailableMinor);
        account.OutstandingAllocations.Sum(value => value.AmountMinor).Should().Be(1000);
        account.AvailableAllocations.Sum(value => value.AmountMinor).Should().Be(700);
        account.OutstandingAllocations.Should().Contain(value => value.OrderId == identity.FirstOrderId
            && value.OrderItemId == identity.FirstItemId && value.StartOrdinal == 2
            && value.UnitCount == 1 && value.MinorPerUnit == 500);
        account.AvailableAllocations.Should().Contain(value => value.OrderId == identity.FirstOrderId
            && value.OrderItemId == identity.FirstItemId && value.StartOrdinal == 2
            && value.UnitCount == 1 && value.MinorPerUnit == 300);

        account.ActiveEqualSharePlan.Should().NotBeNull();
        account.ActiveEqualSharePlan!.PlanId.Should().Be(identity.PlanId);
        account.ActiveEqualSharePlan.TotalMinor.Should().Be(800);
        account.ActiveEqualSharePlan.Scope.Sum(value => value.AmountMinor).Should().Be(800);
        account.ActiveEqualSharePlan.IsOwnPlan.Should().BeTrue();
        account.ActiveEqualSharePlan.Slots.Should().OnlyContain(value => !value.IsAvailable
            && value.ClaimState == null);

        var own = account.ActiveAttempts.Single(value => value.State == AccountPaymentState.Reserved);
        own.IsOwnOperation.Should().BeTrue();
        own.OperationId.Should().Be(identity.OwnOperationId);
        own.AmountMinor.Should().Be(200);
        var other = account.ActiveAttempts.Single(value => value.State == AccountPaymentState.Processing);
        other.IsOwnOperation.Should().BeFalse();
        other.OperationId.Should().BeNull();
        other.AmountMinor.Should().Be(100);

        var json = JsonSerializer.Serialize(account);
        json.Should().NotContain(_actorId.ToString())
            .And.NotContain(identity.OtherActorId.ToString())
            .And.NotContain("private-provider-session")
            .And.NotContain("private-provider-charge")
            .And.NotContain("private-cashier-note")
            .And.NotContain("Private dish name");
    }

    [Fact]
    public async Task Feature_off_returns_not_found_without_resolving_an_actor()
    {
        await using var context = fixture.CreateContext();
        var actors = new Mock<IAccountPaymentActorResolver>();
        var reader = NewReader(context, actors.Object, Features(enabled: false));

        var read = () => reader.GetAsync(Guid.NewGuid(), CancellationToken.None);
        await read.Should().ThrowAsync<NotFoundException>();
        actors.Verify(value => value.ResolveStaffActor(), Times.Never);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Reader_rejects_unauthenticated_or_api_token_admins(bool authenticated, bool isApiToken)
    {
        await using var context = fixture.CreateContext();
        var currentUser = CurrentUser(_actorId, UserRole.Admin, authenticated, isApiToken);
        var reader = NewReader(context, new AccountPaymentActorResolver(currentUser,
            Features(enabled: true), Modules()), Features(enabled: true));

        var read = () => reader.GetAsync(Guid.NewGuid(), CancellationToken.None);
        await read.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Active_attempt_summary_limit_fails_closed_instead_of_omitting_a_reservation()
    {
        var identity = await SeedPartiallyPaidAccount();
        await using var context = fixture.CreateContext();
        var settings = new AccountPaymentSettings { MaximumActiveAttemptSummaries = 1 };
        var reader = NewReader(context, ActorResolver(_actorId), Features(enabled: true), settings);

        var read = () => reader.GetAsync(identity.SessionId, CancellationToken.None);
        await read.Should().ThrowAsync<ConflictException>().WithMessage("*too many active payment attempts*");
    }

    [Fact]
    public async Task Equal_share_slots_keep_reviewed_amounts_and_claims_after_capture_and_a_new_round()
    {
        var identity = await SeedAccount(10m, quantity: 3);
        var plan = await CreatePlan(identity.SessionId, new CreateAccountEqualSharePlanRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            ShareCount = 3
        });
        var last = await CreateQuote(identity.SessionId, EqualQuote(Guid.NewGuid(), plan.PlanId, 3, revision: 1));
        var first = await CreateQuote(identity.SessionId, EqualQuote(Guid.NewGuid(), plan.PlanId, 1, revision: 1));
        await Reserve(identity.SessionId, last.OperationId, expectedVersion: 1, revision: 1);
        await Reserve(identity.SessionId, first.OperationId, expectedVersion: 1, revision: 1);
        await Capture(identity.SessionId, last.OperationId, expectedVersion: 2);

        var nextRound = await SeedOrder(identity.SessionId, 5m);
        await using (var context = fixture.CreateContext())
        {
            var session = await context.TableServiceSessions.SingleAsync(value => value.Id == identity.SessionId);
            session.RecordAccountChange();
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var account = await Reader(readContext).GetAsync(identity.SessionId, CancellationToken.None);
        account.ActiveEqualSharePlan.Should().NotBeNull();
        var summary = account.ActiveEqualSharePlan!;
        summary.PlanId.Should().Be(plan.PlanId);
        summary.AccountRevision.Should().Be(1);
        summary.IsOwnPlan.Should().BeTrue();
        summary.Scope.Should().OnlyContain(value => value.OrderId == identity.OrderId);
        summary.Scope.Should().NotContain(value => value.OrderId == nextRound.OrderId);
        summary.Slots.OrderBy(value => value.Ordinal).Select(value => value.AmountMinor)
            .Should().Equal(330L, 330L, 340L);

        var slots = summary.Slots.ToDictionary(value => value.Ordinal);
        slots[1].ClaimState.Should().Be(AccountPaymentState.Reserved);
        slots[1].IsAvailable.Should().BeFalse();
        slots[2].ClaimState.Should().BeNull();
        slots[2].IsAvailable.Should().BeTrue();
        slots[3].ClaimState.Should().Be(AccountPaymentState.Captured);
        slots[3].IsAvailable.Should().BeFalse();
        account.AvailableAllocations.Should().Contain(value => value.OrderId == nextRound.OrderId);
    }

    private AccountPaymentAccountReader Reader(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context) =>
        NewReader(context, ActorResolver(_actorId), Features(enabled: true));

    private static AccountPaymentAccountReader NewReader(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context,
        IAccountPaymentActorResolver actorResolver,
        ITenantFeatures features,
        AccountPaymentSettings? settings = null) =>
        new(context, actorResolver, Mock.Of<ITableGuestParticipantPaymentAuthorization>(),
            GuestPolicy(features), features, Options.Create(settings ?? new AccountPaymentSettings()));

    private static GuestAccountPaymentPolicy GuestPolicy(ITenantFeatures features) => new(
        features,
        Mock.Of<ITenantModules>(value => value.IsEnabled(ModuleIds.OnlinePayments)),
        Mock.Of<IStripeGateway>(value => value.IsConfigured),
        Options.Create(new AccountOnlineContributionSettings { SettlementCurrency = "CHF" }));

    private async Task<(Guid SessionId, Guid OrderId)> SeedAccount(decimal total, int quantity)
    {
        await using var context = fixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            Currency = "CHF",
            AccountRevision = 1,
            OpenedAt = DateTime.UtcNow,
            CreatedBy = _actorId.ToString()
        };
        var order = CreateOrder(session.Id, "Equal-share reader", total);
        var item = CreateItem(order.Id, total, quantity);
        order.Items.Add(item);
        context.TableServiceSessions.Add(session);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (session.Id, order.Id);
    }

    private async Task<(Guid OrderId, Guid ItemId)> SeedOrder(Guid sessionId, decimal total)
    {
        await using var context = fixture.CreateContext();
        var order = CreateOrder(sessionId, "EqualNext", total);
        var item = CreateItem(order.Id, total, 1);
        order.Items.Add(item);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (order.Id, item.Id);
    }

    private async Task<AccountEqualSharePlanDto> CreatePlan(
        Guid sessionId, CreateAccountEqualSharePlanRequest request)
    {
        await using var context = fixture.CreateContext();
        var features = Features(enabled: true);
        return await new AccountEqualSharePlanService(context, ActorResolver(_actorId),
                Mock.Of<ITableGuestParticipantPaymentAuthorization>(), GuestPolicy(features), features,
                Options.Create(new AccountPaymentSettings()), TimeProvider.System)
            .CreateAsync(sessionId, request, CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> CreateQuote(
        Guid sessionId, CreateAccountPaymentQuoteRequest request)
    {
        await using var context = fixture.CreateContext();
        var features = Features(enabled: true);
        return await new AccountPaymentQuoteService(context, ActorResolver(_actorId),
                Mock.Of<ITableGuestParticipantPaymentAuthorization>(), GuestPolicy(features), features,
                Options.Create(new AccountPaymentSettings()), TimeProvider.System)
            .CreateQuoteAsync(sessionId, request, CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> Reserve(
        Guid sessionId, Guid operationId, int expectedVersion, long revision)
    {
        await using var context = fixture.CreateContext();
        var features = Features(enabled: true);
        return await new AccountPaymentReservationService(context, ActorResolver(_actorId),
                Mock.Of<ITableGuestParticipantPaymentAuthorization>(), GuestPolicy(features), features,
                Options.Create(new AccountPaymentSettings()), TimeProvider.System)
            .ReserveAsync(sessionId, operationId,
                new ReserveAccountPaymentRequest { ExpectedVersion = expectedVersion, ExpectedAccountRevision = revision },
                CancellationToken.None);
    }

    private async Task<AccountPaymentOperationDto> Capture(Guid sessionId, Guid operationId, int expectedVersion)
    {
        await using var context = fixture.CreateContext();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(value => value.GetAuditIdentifier()).Returns(_actorId.ToString());
        var fidelity = new Mock<IOrderFidelityCoordinator>();
        fidelity.Setup(value => value.AwardEarnedPointsAsync(It.IsAny<Order>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new AccountPaymentCaptureService(context, ActorResolver(_actorId),
            new AccountPaymentCaptureWriter(context, currentUser.Object, TimeProvider.System),
            fidelity.Object, TimeProvider.System, NullLogger<AccountPaymentCaptureService>.Instance);
        return await service.CaptureManualAsync(sessionId, operationId,
            new CaptureAccountPaymentRequest { ExpectedVersion = expectedVersion, ReceivedMinor = 340 }, CancellationToken.None);
    }

    private static CreateAccountPaymentQuoteRequest EqualQuote(
        Guid operationId, Guid planId, int ordinal, long revision) => new()
        {
            OperationId = operationId,
            ExpectedAccountRevision = revision,
            Mode = AccountPaymentMode.Equal,
            PaymentMethod = PaymentMethod.Cash,
            EqualSharePlanId = planId,
            EqualShareOrdinal = ordinal
        };

    private async Task<SeededAccount> SeedPartiallyPaidAccount()
    {
        var otherActorId = Guid.NewGuid();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            Currency = "CHF",
            AccountRevision = 5,
            OpenedAt = DateTime.UtcNow,
            CreatedBy = "private-cashier-note"
        };
        var firstOrder = CreateOrder(session.Id, "Account read first", 10m);
        var firstItem = CreateItem(firstOrder.Id, 10m, 2);
        firstOrder.Items.Add(firstItem);
        firstOrder.Payments.Add(Payment(firstOrder.Id, 2m, PaymentMethod.Cash));
        var capturedTender = Payment(firstOrder.Id, 3m, PaymentMethod.Cash);
        firstOrder.Payments.Add(capturedTender);
        firstOrder.TotalPaid = 5m;
        firstOrder.RemainingAmount = 5m;
        firstOrder.PaymentStatus = PaymentStatus.PartiallyPaid;

        var secondOrder = CreateOrder(session.Id, "Account read second", 5m);
        var secondItem = CreateItem(secondOrder.Id, 5m, 1);
        secondOrder.Items.Add(secondItem);

        var planId = Guid.NewGuid();
        var planScope = new[]
        {
            new AccountDebtSegment(firstOrder.Id, firstItem.Id, 1, 1, 300),
            new AccountDebtSegment(firstOrder.Id, firstItem.Id, 2, 1, 500)
        };
        var plan = new AccountEqualSharePlan
        {
            Id = planId,
            ServiceSessionId = session.Id,
            OperationId = Guid.NewGuid(),
            AccountRevision = 1,
            TotalMinor = 800,
            ShareCount = 2,
            Currency = "CHF",
            PayloadHash = new string('a', 64),
            ScopeJson = AccountPaymentSnapshots.Serialize(planScope),
            ActorId = _actorId,
            ActorKind = AccountPaymentActorKind.Staff,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _actorId.ToString()
        };

        var capturedAttemptId = Guid.NewGuid();
        var capturedAttempt = CreateAttempt(session.Id, capturedAttemptId, _actorId,
            AccountPaymentState.Captured, AccountPaymentMode.Items, 300);
        capturedAttempt.CompletedAt = DateTime.UtcNow;
        capturedAttempt.Allocations.Add(Allocation(capturedAttemptId, firstOrder.Id, firstItem.Id,
            capturedTender.Id, start: 1, units: 1, minorPerUnit: 300));

        var ownOperationId = Guid.NewGuid();
        var ownReservedId = Guid.NewGuid();
        var ownReserved = CreateAttempt(session.Id, ownReservedId, _actorId,
            AccountPaymentState.Reserved, AccountPaymentMode.Amount, 200);
        ownReserved.ReservationExpiresAt = DateTime.UtcNow.AddMinutes(5);
        ownReserved.Allocations.Add(Allocation(ownReservedId, firstOrder.Id, firstItem.Id,
            paymentId: null, start: 2, units: 1, minorPerUnit: 200));
        ownReserved.OperationId = ownOperationId;

        var otherOperationId = Guid.NewGuid();
        var otherPendingId = Guid.NewGuid();
        var otherPending = CreateAttempt(session.Id, otherPendingId, otherActorId,
            AccountPaymentState.Processing, AccountPaymentMode.Amount, 100);
        otherPending.PaymentMethod = PaymentMethod.OnlinePayment;
        otherPending.ProviderSessionId = "private-provider-session";
        otherPending.ProviderChargeId = "private-provider-charge";
        otherPending.OperationId = otherOperationId;
        otherPending.Allocations.Add(Allocation(otherPendingId, secondOrder.Id, secondItem.Id,
            paymentId: null, start: 1, units: 1, minorPerUnit: 100));

        await using var context = fixture.CreateContext();
        context.TableServiceSessions.Add(session);
        context.Orders.AddRange(firstOrder, secondOrder);
        context.OrderPayments.AddRange(firstOrder.Payments);
        context.AccountEqualSharePlans.Add(plan);
        context.AccountPaymentAttempts.AddRange(capturedAttempt, ownReserved, otherPending);
        await context.SaveChangesAsync();
        return new(session.Id, firstOrder.Id, firstItem.Id, planId, ownOperationId, otherActorId);
    }

    private AccountPaymentActorResolver ActorResolver(Guid actorId) => new(CurrentUser(actorId,
        UserRole.Cashier, authenticated: true, isApiToken: false), Features(enabled: true), Modules());

    private static ITenantModules Modules()
    {
        var modules = new Mock<ITenantModules>();
        modules.Setup(value => value.IsEnabled(It.IsAny<string>())).Returns(true);
        return modules.Object;
    }

    private static ICurrentUserService CurrentUser(
        Guid actorId, UserRole role, bool authenticated, bool isApiToken)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(value => value.UserId).Returns(actorId);
        currentUser.Setup(value => value.Role).Returns(role);
        currentUser.Setup(value => value.IsAuthenticated).Returns(authenticated);
        currentUser.Setup(value => value.IsApiToken).Returns(isApiToken);
        currentUser.Setup(value => value.GetAuditIdentifier()).Returns(actorId.ToString());
        return currentUser.Object;
    }

    private static TenantFeatures Features(bool enabled) =>
        new(Options.Create(new TenantFeatureSettings { TableAccountPaymentsV1 = enabled }));

    private static Order CreateOrder(Guid sessionId, string number, decimal total) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = number,
        ServiceSessionId = sessionId,
        Type = OrderType.DineIn,
        Status = OrderStatus.Completed,
        PaymentStatus = PaymentStatus.Pending,
        SubTotal = total,
        Total = total,
        RemainingAmount = total,
        OrderDate = DateTime.UtcNow,
        CreatedBy = "private-cashier-note"
    };

    private static OrderItem CreateItem(Guid orderId, decimal total, int quantity) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        ProductName = "Private dish name",
        Quantity = quantity,
        UnitPrice = total / quantity,
        ItemTotal = total,
        SpecialInstructions = "private-cashier-note",
        CreatedBy = "private-cashier-note"
    };

    private static OrderPayment Payment(Guid orderId, decimal amount, PaymentMethod method) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Amount = amount,
        Currency = "CHF",
        PaymentMethod = method,
        Status = PaymentStatus.Completed,
        PaymentDate = DateTime.UtcNow,
        CreatedBy = "private-cashier-note"
    };

    private AccountPaymentAttempt CreateAttempt(
        Guid sessionId, Guid id, Guid actorId, AccountPaymentState state, AccountPaymentMode mode, long amount) => new()
        {
            Id = id,
            ServiceSessionId = sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = actorId,
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = mode,
            State = state,
            PaymentMethod = PaymentMethod.Cash,
            Version = 2,
            ExpectedAccountRevision = 1,
            AmountMinor = amount,
            Currency = "CHF",
            PayloadHash = new string('b', 64),
            SnapshotJson = "{}",
            QuoteExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId.ToString()
        };

    private static AccountPaymentAllocation Allocation(
        Guid attemptId, Guid orderId, Guid? itemId, Guid? paymentId,
        int start, int units, long minorPerUnit) => new()
        {
            Id = Guid.NewGuid(),
            AttemptId = attemptId,
            OrderId = orderId,
            OrderItemId = itemId,
            OrderPaymentId = paymentId,
            StartOrdinal = start,
            UnitCount = units,
            MinorPerUnit = minorPerUnit,
            AmountMinor = checked(minorPerUnit * units),
            CreatedBy = "private-cashier-note"
        };

    private sealed record SeededAccount(
        Guid SessionId, Guid FirstOrderId, Guid FirstItemId, Guid PlanId, Guid OwnOperationId, Guid OtherActorId);
}
