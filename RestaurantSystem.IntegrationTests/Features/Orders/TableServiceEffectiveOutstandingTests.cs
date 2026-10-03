using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.ServerWorkspace.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class TableServiceEffectiveOutstandingTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string AuditIdentity = nameof(TableServiceEffectiveOutstandingTests);

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Payment_writer_handoff_and_session_read_use_effective_charge_when_cache_is_stale()
    {
        var visit = await SeedCreditedVisitAsync(credit: 10m, staleRemaining: 20m);
        await using var context = fixture.CreateContext();
        (await TableServicePaymentHandoffRules.ReadOutstandingAsync(
            context, visit.SessionId, CancellationToken.None)).Should().Be(10m);

        var current = User();
        var capturedTenders = new List<OrderPaymentTender>();
        var applicator = new Mock<IOrderPaymentApplicator>();
        applicator.Setup(value => value.ApplyToOrderAsync(
                It.IsAny<Guid>(), It.IsAny<OrderPaymentTender>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, OrderPaymentTender, CancellationToken>((_, tender, _) => capturedTenders.Add(tender))
            .ReturnsAsync(PaymentApplicationResult.Applied(new Order { CreatedBy = AuditIdentity }));
        var writer = new TableServiceSessionPaymentWriter(context, applicator.Object, current);
        var session = await context.TableServiceSessions.SingleAsync(value => value.Id == visit.SessionId);

        await using var transaction = await context.Database.BeginTransactionAsync();
        var overCollection = await writer.ApplyAsync(session, Payment(visit.SessionId, 15m), CancellationToken.None);
        overCollection.Success.Should().BeFalse();
        applicator.Verify(value => value.ApplyToOrderAsync(
            It.IsAny<Guid>(), It.IsAny<OrderPaymentTender>(), It.IsAny<CancellationToken>()), Times.Never);

        var exactCollection = await writer.ApplyAsync(session, Payment(visit.SessionId, 10m), CancellationToken.None);
        exactCollection.Success.Should().BeTrue();
        exactCollection.Applied.Should().Be(10m);
        capturedTenders.Should().ContainSingle().Which.Amount.Should().Be(10m);
        await transaction.RollbackAsync();

        await using var readContext = fixture.CreateContext();
        var account = await Reader(readContext).ReadAsync(visit.SessionId, CancellationToken.None);
        account.Should().NotBeNull();
        account!.Outstanding.Should().Be(10m);
        account.EligibleOutstanding.Should().Be(10m);
        account.Bill.Remaining.Should().Be(10m);
        account.CanClose.Should().BeFalse();
    }

    [Fact]
    public async Task Flag_off_close_accepts_a_fully_credited_order_despite_stale_remaining_cache()
    {
        var visit = await SeedCreditedVisitAsync(credit: 20m, staleRemaining: 20m);
        await using var context = fixture.CreateContext();
        var reader = Reader(context);
        var beforeClose = await reader.ReadAsync(visit.SessionId, CancellationToken.None);
        beforeClose.Should().NotBeNull();
        beforeClose!.Outstanding.Should().Be(0m);
        beforeClose.Bill.Remaining.Should().Be(0m);
        beforeClose.CanClose.Should().BeTrue();

        var features = Mock.Of<ITenantFeatures>(value => value.TableAccountPaymentsV1 == false);
        var result = await new CloseTableServiceSessionCommandHandler(
            context, reader, new TableGuestVisitRevoker(context), features: features).Handle(
            new CloseTableServiceSessionCommand
            {
                ServiceSessionId = visit.SessionId,
                ExpectedVersion = 1
            }, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        result.Data!.Status.Should().Be(nameof(TableServiceSessionStatus.Closed));
    }

    [Fact]
    public void Floor_projection_uses_derived_member_and_legacy_debt_and_versions_credit_changes()
    {
        var now = DateTime.UtcNow;
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(value => value.Role).Returns(UserRole.Server);
        var clock = new Mock<ITenantClock>();
        clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.Utc);
        var projector = new ServerFloorSnapshotProjector(clock.Object, current.Object, 0.01m);
        var memberTable = Table("F-1");
        var legacyTable = Table("F-2");
        var sessionId = Guid.NewGuid();
        var memberOrder = new OrderDto
        {
            Id = Guid.NewGuid(),
            OrderNumber = "FLOOR-MEMBER",
            Status = nameof(OrderStatus.Completed),
            PaymentStatus = nameof(PaymentStatus.Pending),
            Total = 20m,
            BillingCreditAmount = 10m,
            TotalPaid = 0m,
            RemainingAmount = 20m
        };
        var bill = new TableBillDto
        {
            ServiceSessionId = sessionId,
            Total = 10m,
            TotalPaid = 0m,
            Remaining = 20m,
            EligibleOutstanding = 10m,
            Rounds = [new TableBillRoundDto { Order = memberOrder, Outstanding = 10m, CanCollect = true }]
        };
        var memberRow = new FloorOrderRow(memberOrder.Id, sessionId, memberTable.Id, null,
            OrderStatus.Completed, 20m, 10m, 0m, 20m, true);
        var legacyRow = new FloorOrderRow(Guid.NewGuid(), null, legacyTable.Id, null,
            OrderStatus.Completed, 20m, 10m, 0m, 20m, true);
        var session = new FloorSessionRow(sessionId, memberTable.Id, null, "CHF", 1, now, bill);

        var projected = Project(projector, [memberTable, legacyTable], [session], [memberRow, legacyRow], now);

        projected.Tables.Single(table => table.TableId == memberTable.Id).Session!.Remaining.Should().Be(10m);
        projected.Tables.Single(table => table.TableId == memberTable.Id).Session!.CanClose.Should().BeFalse();
        projected.Tables.Single(table => table.TableId == legacyTable.Id).Legacy!.Outstanding.Should().Be(10m);

        var changedCredit = legacyRow with { BillingCreditAmount = 9m };
        var afterCreditChange = Project(projector, [memberTable, legacyTable], [session],
            [memberRow, changedCredit], now);
        afterCreditChange.Version.Should().NotBe(projected.Version);
    }

    private async Task<(Guid SessionId, Guid OrderId)> SeedCreditedVisitAsync(
        decimal credit, decimal staleRemaining)
    {
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var amendmentId = Guid.NewGuid();
        var creditMinor = checked((long)(credit * 100m));
        var resolution = new OrderAmendmentFinancialPreviewDto(
            "CHF", 0, creditMinor, -creditMinor, creditMinor,
            OrderAmendmentFinancialResolutionStatus.Resolved,
            OrderAmendmentCreditState.BalanceReduction,
            OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.None);

        await using var context = fixture.CreateContext();
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = sessionId,
            TableNumber = 84,
            Currency = "CHF",
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            AccountRevision = 1,
            OpenedAt = now,
            CreatedAt = now,
            CreatedBy = AuditIdentity
        });
        var source = new Order
        {
            Id = orderId,
            OrderNumber = $"EFFECTIVE-{Guid.NewGuid():N}"[..18],
            Type = OrderType.DineIn,
            TableNumber = 84,
            ServiceSessionId = sessionId,
            SubTotal = 20m,
            Total = 20m,
            BillingCreditAmount = credit,
            TotalPaid = 0m,
            RemainingAmount = staleRemaining,
            Status = OrderStatus.Completed,
            PaymentStatus = credit >= 20m ? PaymentStatus.Completed : PaymentStatus.Pending,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = AuditIdentity
        };
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductName = "Two servings",
            Quantity = 2,
            UnitPrice = 10m,
            ItemTotal = 20m,
            CreatedAt = now,
            CreatedBy = AuditIdentity
        };
        source.Items.Add(item);
        context.Orders.Add(source);
        var snapshot = new OrderItemDto
        {
            Id = item.Id,
            ProductName = item.ProductName,
            Quantity = 2,
            UnitPrice = 10m,
            ItemTotal = 20m,
            Kind = item.Kind
        };
        var removedQuantity = checked((int)(credit / 10m));
        var changes = new[]
        {
            new OrderAmendmentChangeSnapshot(item.Id, OrderAmendmentChangeKind.Void,
                1, removedQuantity, false, snapshot with { Quantity = removedQuantity }, null)
        };
        context.OrderAmendments.Add(new OrderAmendment
        {
            Id = amendmentId,
            SourceOrderId = orderId,
            ServiceSessionId = sessionId,
            ActorUserId = actorId,
            ActorRole = UserRole.Server.ToString(),
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            CommitPayloadHash = new string('b', 64),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            CommittedAccountRevision = 2,
            ExpiresAt = now,
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = OrderAmendmentJson.Serialize(changes),
            SourceSnapshotJson = OrderAmendmentJson.Serialize(new OrderAmendmentSourceSnapshot(
                source.Id, source.OrderNumber, source.Type, source.Status, source.IsKitchenReleased,
                sessionId, source.Version, "CHF", source.Total, [snapshot])),
            FinancialResolutionJson = OrderAmendmentJson.Serialize(resolution),
            CreatedAt = now,
            CreatedBy = AuditIdentity
        });
        context.OrderBillingCredits.Add(new OrderBillingCredit
        {
            Id = Guid.NewGuid(),
            SourceOrderId = orderId,
            AmendmentId = amendmentId,
            AmountMinor = creditMinor,
            Currency = "CHF",
            ActorUserId = actorId,
            ActorRole = UserRole.Server.ToString(),
            CreatedAt = now,
            CreatedBy = AuditIdentity
        });
        await context.SaveChangesAsync();
        return (sessionId, orderId);
    }

    private static AddTableServiceSessionPaymentCommand Payment(Guid sessionId, decimal amount) => new()
    {
        ServiceSessionId = sessionId,
        ExpectedVersion = 1,
        OperationId = Guid.NewGuid(),
        PaymentMethod = PaymentMethod.Cash,
        Amount = amount,
        Currency = "CHF"
    };

    private static ICurrentUserService User() => Mock.Of<ICurrentUserService>(
        value => value.GetAuditIdentifier() == AuditIdentity);

    private static TableServiceSessionReader Reader(ApplicationDbContext context)
    {
        var mapping = new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
            NullLogger<OrderMappingService>.Instance);
        return new TableServiceSessionReader(context,
            new TableBillAssembler(context, mapping, NullLogger<TableBillAssembler>.Instance));
    }

    private static ServerFloorProjection Project(ServerFloorSnapshotProjector projector,
        IReadOnlyCollection<Table> tables, IReadOnlyCollection<FloorSessionRow> sessions,
        IReadOnlyCollection<FloorOrderRow> orders, DateTime now) => projector.Project(
        new ServerFloorSnapshotInput(
            Array.Empty<RestaurantSystem.Domain.Entities.FloorPlan>(), tables, sessions, orders,
            Array.Empty<ReservationRow>(), new DateTimeOffset(now, TimeSpan.Zero), now));

    private static Table Table(string label) => new()
    {
        Id = Guid.NewGuid(),
        TableNumber = label,
        IsActive = true,
        MaxGuests = 4,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = AuditIdentity
    };
}
