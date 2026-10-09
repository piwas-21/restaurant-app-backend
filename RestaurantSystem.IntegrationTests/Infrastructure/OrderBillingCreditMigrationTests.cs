using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class OrderBillingCreditMigrationTests : IAsyncLifetime
{
    private const string MigrationBeforeCredits = "20261003064057_AddAccountCheckoutJournal";
    private readonly DatabaseFixture _fixture;

    public OrderBillingCreditMigrationTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Compatibility_backfill_preserves_legacy_rows_and_materializes_only_proven_food_credits()
    {
        var actorId = Guid.Parse("6bba7a53-a6c8-493f-82fd-16ac981f57b8");
        CreditProbe eligible;
        CreditProbe tendered;
        var probes = new List<CreditProbe>();
        TableServiceSession unusedVisit;
        TableServiceSession unpaidOrderVisit;
        Order unpaidOrder;

        await using (var setup = _fixture.CreateContext())
        {
            eligible = AddProbe(setup, actorId, "CHF", "CHF");
            probes.Add(eligible);
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF", tax: 1m));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF", tip: 1m));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF", fidelityDiscount: 1m));
            tendered = AddProbe(setup, actorId, "CHF", "CHF", hasTender: true);
            probes.Add(tendered);
            probes.Add(AddProbe(setup, actorId, "CHF", "EUR"));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(EmptySourceSnapshot: true)));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(EmptyChanges: true)));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(SnapshotOrderId: Guid.NewGuid())));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(SnapshotTotal: 21m)));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(SnapshotItemQuantity: 1)));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(ChangeKind: OrderAmendmentChangeKind.InstructionChange)));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(ChangeStartOrdinal: 3)));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(DuplicateVoidRanges: true, ClaimedCreditMinor: 2_000)));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(ClaimedCreditMinor: 500)));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(AddSecondCommittedAmendment: true)));
            probes.Add(AddProbe(setup, actorId, "CHF", "CHF",
                corruption: new ProbeCorruption(OrderDiscount: 1m)));

            unusedVisit = NewSession("CHF");
            unpaidOrderVisit = NewSession("CHF");
            unpaidOrder = NewOrder(unpaidOrderVisit.Id, tax: 0m, tip: 0m, fidelityDiscount: 0m);
            setup.TableServiceSessions.AddRange(unusedVisit, unpaidOrderVisit);
            setup.Orders.Add(unpaidOrder);
            await setup.SaveChangesAsync();
        }

        try
        {
            await MigrateToAsync(MigrationBeforeCredits);

            (await RawRelationMissingAsync("order_billing_credits")).Should().BeTrue();
            foreach (var probe in probes)
            {
                (await ReadLegacyOrderAsync(probe.Order.Id)).Should().Be(
                    new LegacyOrderSnapshot(probe.Order.Total, probe.Order.Total, nameof(PaymentStatus.Pending)),
                    "Down must preserve the original charge summary");
                (await FinancialJsonMatchesAsync(probe.Amendment.Id, probe.FinancialJson)).Should().BeTrue(
                    "the full committed financial snapshot must survive the old schema");
            }

            (await ReadLegacyOrderAsync(unpaidOrder.Id)).Should().Be(
                new LegacyOrderSnapshot(20m, 20m, nameof(PaymentStatus.Pending)));
            (await ReadTenderAsync(tendered.Order.Id)).Should().Be(
                new LegacyTenderSnapshot("Cash", 1.25m, "Pending", "migration-literal-tender", "CHF"));

            await MigrateToLatestAsync();

            await using (var verify = _fixture.CreateContext())
            {
                var credits = await verify.OrderBillingCredits.AsNoTracking().ToListAsync();
                credits.Should().ContainSingle();
                credits[0].SourceOrderId.Should().Be(eligible.Order.Id);
                credits[0].AmendmentId.Should().Be(eligible.Amendment.Id);
                credits[0].AmountMinor.Should().Be(1_000);
                credits[0].Currency.Should().Be("CHF");
                credits[0].ActorUserId.Should().Be(actorId);
                credits[0].ActorRole.Should().Be("Cashier");

                var creditedOrder = await ReadOrderAsync(verify, eligible.Order.Id);
                creditedOrder.Total.Should().Be(20m);
                creditedOrder.BillingCreditAmount.Should().Be(10m);
                creditedOrder.RemainingAmount.Should().Be(10m);
                creditedOrder.PaymentStatus.Should().Be(PaymentStatus.Pending);

                foreach (var rejected in probes.Where(probe => probe.Amendment.Id != eligible.Amendment.Id))
                {
                    var order = await ReadOrderAsync(verify, rejected.Order.Id);
                    order.Total.Should().Be(rejected.Order.Total);
                    order.BillingCreditAmount.Should().Be(0m);
                    order.RemainingAmount.Should().Be(rejected.Order.Total);
                }

                foreach (var probe in probes)
                {
                    (await verify.TableServiceSessions.AsNoTracking()
                        .Where(value => value.Id == probe.Session.Id)
                        .Select(value => value.BillingAllocationVersion)
                        .SingleAsync()).Should().Be(0,
                            "committed order and amendment history must retain the legacy allocation model");
                }

                (await verify.TableServiceSessions.AsNoTracking()
                    .Where(value => value.Id == unusedVisit.Id)
                    .Select(value => value.BillingAllocationVersion).SingleAsync()).Should().Be(1);
                (await verify.TableServiceSessions.AsNoTracking()
                    .Where(value => value.Id == unpaidOrderVisit.Id)
                    .Select(value => value.BillingAllocationVersion).SingleAsync()).Should().Be(0);
            }

            var down = () => MigrateToAsync(MigrationBeforeCredits);
            await down.Should().ThrowAsync<PostgresException>()
                .WithMessage("*Billing history exists*");

            await using var afterRefusal = _fixture.CreateContext();
            (await afterRefusal.OrderBillingCredits.CountAsync()).Should().Be(1,
                "a refused rollback must retain the append-only money evidence");
            (await ReadOrderAsync(afterRefusal, eligible.Order.Id)).BillingCreditAmount.Should().Be(10m);
        }
        finally
        {
            await MigrateToLatestAsync();
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 200)]
    public async Task Down_refuses_current_unpaid_orders_even_without_tender(decimal tip, long tipMinor)
    {
        var visit = NewSession("CHF");
        visit.BillingAllocationVersion = 1;
        var order = NewOrder(visit.Id, tax: 0m, tip: tip, fidelityDiscount: 0m,
            total: 10m + tip, itemTotal: 10m, itemQuantity: 1, subTotal: 10m);
        await using (var setup = _fixture.CreateContext())
        {
            setup.TableServiceSessions.Add(visit);
            setup.Orders.Add(order);
            await setup.SaveChangesAsync();
        }
        try
        {
            var down = () => MigrateToAsync(MigrationBeforeCredits);
            await down.Should().ThrowAsync<PostgresException>()
                .WithMessage("*Current visit allocation history exists*");
            await using var verify = _fixture.CreateContext();
            (await verify.TableServiceSessions.AsNoTracking().Where(value => value.Id == visit.Id)
                .Select(value => value.BillingAllocationVersion).SingleAsync()).Should().Be(1);
            var unchanged = await verify.Orders.IgnoreAutoIncludes().AsNoTracking()
                .Where(value => value.Id == order.Id).Select(value => new Order
                {
                    Id = value.Id,
                    Total = value.Total,
                    Tip = value.Tip,
                    CreatedBy = value.CreatedBy,
                    DeliveryFee = value.DeliveryFee,
                    TotalPaid = value.TotalPaid,
                    Items = value.Items.Select(item => new OrderItem
                    {
                        Id = item.Id,
                        OrderId = item.OrderId,
                        CreatedBy = item.CreatedBy,
                        ParentOrderItemId = item.ParentOrderItemId,
                        Quantity = item.Quantity,
                        ItemTotal = item.ItemTotal
                    }).ToList()
                }).SingleAsync();
            unchanged.TotalPaid.Should().Be(0m);
            (await verify.OrderPayments.CountAsync(value => value.OrderId == order.Id)).Should().Be(0);
            var charges = FrozenOrderChargeMath.Read(unchanged, new AccountMoney("CHF"));
            charges.FoodMinor.Should().Be(1000, "the unpaid item must keep its CHF 10 food scope");
            charges.TipMinor.Should().Be(tipMinor, "the retained tip must stay outside item payment");
        }
        finally
        {
            await MigrateToLatestAsync();
        }
    }

    [Fact]
    public async Task Down_refuses_to_remove_a_promoted_visit_after_it_records_a_tender()
    {
        var visit = NewSession("CHF");
        await using (var setup = _fixture.CreateContext())
        {
            setup.TableServiceSessions.Add(visit);
            await setup.SaveChangesAsync();
        }

        try
        {
            await MigrateToAsync(MigrationBeforeCredits);
            await MigrateToLatestAsync();

            Guid orderId;
            await using (var addTender = _fixture.CreateContext())
            {
                var currentVisit = await addTender.TableServiceSessions.SingleAsync(value => value.Id == visit.Id);
                currentVisit.BillingAllocationVersion.Should().Be(1,
                    "the empty legacy visit was promoted by the compatibility migration");

                var order = NewOrder(visit.Id, tax: 0m, tip: 0m, fidelityDiscount: 0m,
                    total: 1m, itemTotal: 1m, itemQuantity: 1, subTotal: 1m);
                order.TotalPaid = 1m;
                order.RemainingAmount = 0m;
                order.PaymentStatus = PaymentStatus.Completed;
                addTender.Orders.Add(order);
                addTender.OrderPayments.Add(new OrderPayment
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    PaymentMethod = PaymentMethod.Cash,
                    Amount = 1m,
                    Status = PaymentStatus.Completed,
                    PaymentDate = DateTime.UtcNow,
                    TransactionId = "readiness-migration-tender",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = nameof(OrderBillingCreditMigrationTests)
                });
                await addTender.SaveChangesAsync();
                orderId = order.Id;
            }

            var down = () => MigrateToAsync(MigrationBeforeCredits);
            await down.Should().ThrowAsync<PostgresException>()
                .WithMessage("*Current visit allocation history exists*");

            await using var verify = _fixture.CreateContext();
            (await verify.TableServiceSessions.AsNoTracking()
                .Where(value => value.Id == visit.Id)
                .Select(value => value.BillingAllocationVersion).SingleAsync()).Should().Be(1);
            var orderWithTender = await verify.Orders.IgnoreAutoIncludes().SingleAsync(value => value.Id == orderId);
            orderWithTender.TotalPaid.Should().Be(1m);
            orderWithTender.RemainingAmount.Should().Be(0m);
            (await verify.OrderPayments.CountAsync(value => value.OrderId == orderId)).Should().Be(1);
        }
        finally
        {
            await MigrateToLatestAsync();
        }
    }

    private static CreditProbe AddProbe(
        ApplicationDbContext context,
        Guid actorId,
        string visitCurrency,
        string financialCurrency,
        decimal tax = 0m,
        decimal tip = 0m,
        decimal fidelityDiscount = 0m,
        bool hasTender = false,
        ProbeCorruption? corruption = null)
    {
        var shape = corruption ?? new ProbeCorruption();
        var session = NewSession(visitCurrency);
        var total = 20m + tax + tip - fidelityDiscount - shape.OrderDiscount;
        var order = NewOrder(session.Id, tax, tip, fidelityDiscount, total, 20m, 2, 20m, shape.OrderDiscount);
        var item = order.Items.Single();
        var sourceSnapshot = new OrderAmendmentSourceSnapshot(
            shape.SnapshotOrderId ?? order.Id,
            order.OrderNumber,
            order.Type,
            order.Status,
            order.IsKitchenReleased,
            session.Id,
            order.Version,
            session.Currency,
            shape.SnapshotTotal ?? order.Total,
            [SnapshotItem(item, shape.SnapshotItemQuantity ?? item.Quantity)]);
        var sourceSnapshotJson = shape.EmptySourceSnapshot
            ? "{}"
            : OrderAmendmentJson.Serialize(sourceSnapshot);
        var changes = BuildChanges(item, shape);
        var changesJson = shape.EmptyChanges ? "[]" : OrderAmendmentJson.Serialize(changes);
        var financialJson = FinancialJson(financialCurrency, shape.ClaimedCreditMinor);
        var amendment = new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = order.Id,
            ServiceSessionId = session.Id,
            ActorUserId = actorId,
            ActorRole = "Cashier",
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            ExpectedOrderVersion = 1,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CommittedAt = DateTime.UtcNow,
            RequestJson = "{}",
            ChangesJson = changesJson,
            SourceSnapshotJson = sourceSnapshotJson,
            FinancialResolutionJson = financialJson,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(OrderBillingCreditMigrationTests)
        };
        if (shape.AddSecondCommittedAmendment)
        {
            var secondSnapshot = OrderAmendmentJson.Serialize(sourceSnapshot with
            {
                Version = order.Version + 1
            });
            context.OrderAmendments.Add(new OrderAmendment
            {
                Id = Guid.NewGuid(),
                SourceOrderId = order.Id,
                ServiceSessionId = session.Id,
                ActorUserId = actorId,
                ActorRole = "Cashier",
                State = OrderAmendmentState.Committed,
                PayloadHash = new string('b', 64),
                ExpectedOrderVersion = order.Version + 1,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                CommittedAt = DateTime.UtcNow,
                RequestJson = "{}",
                ChangesJson = changesJson,
                SourceSnapshotJson = secondSnapshot,
                FinancialResolutionJson = financialJson,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = nameof(OrderBillingCreditMigrationTests)
            });
        }

        context.TableServiceSessions.Add(session);
        context.Orders.Add(order);
        context.OrderAmendments.Add(amendment);
        if (hasTender)
        {
            context.OrderPayments.Add(new OrderPayment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 1.25m,
                Status = PaymentStatus.Pending,
                TransactionId = "migration-literal-tender",
                Currency = "CHF",
                PaymentDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = nameof(OrderBillingCreditMigrationTests)
            });
        }

        return new CreditProbe(session, order, amendment, financialJson);
    }

    private static List<OrderAmendmentChangeSnapshot> BuildChanges(OrderItem item, ProbeCorruption shape)
    {
        var current = shape.ChangeKind == OrderAmendmentChangeKind.InstructionChange
            ? SnapshotItem(item, specialInstructions: "No onions")
            : null;
        var first = new OrderAmendmentChangeSnapshot(
            item.Id,
            shape.ChangeKind,
            shape.ChangeStartOrdinal,
            1,
            false,
            SnapshotItem(item, shape.ChangeKind == OrderAmendmentChangeKind.Void ? 1 : item.Quantity),
            current);
        return shape.DuplicateVoidRanges ? [first, first] : [first];
    }

    private static OrderItemDto SnapshotItem(
        OrderItem item,
        int? quantity = null,
        string? specialInstructions = null) => new()
        {
            Id = item.Id,
            ProductName = item.ProductName,
            Quantity = quantity ?? item.Quantity,
            UnitPrice = item.UnitPrice,
            ItemTotal = item.ItemTotal,
            SpecialInstructions = specialInstructions
        };

    private static TableServiceSession NewSession(string currency) => new()
    {
        Id = Guid.NewGuid(),
        Currency = currency,
        Status = TableServiceSessionStatus.Open,
        Version = 1,
        AccountRevision = 1,
        BillingAllocationVersion = 0,
        OpenedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(OrderBillingCreditMigrationTests)
    };

    private static Order NewOrder(
        Guid sessionId,
        decimal tax,
        decimal tip,
        decimal fidelityDiscount,
        decimal total = 20m,
        decimal itemTotal = 20m,
        int itemQuantity = 2,
        decimal? subTotal = null,
        decimal discount = 0m)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"BCR-{Guid.NewGuid():N}"[..20],
            Type = OrderType.DineIn,
            ServiceSessionId = sessionId,
            SubTotal = subTotal ?? itemTotal,
            Tax = tax,
            Tip = tip,
            Discount = discount,
            Total = total,
            TotalPaid = 0m,
            RemainingAmount = total,
            FidelityPointsDiscount = fidelityDiscount,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(OrderBillingCreditMigrationTests)
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductName = "Migration source item",
            Quantity = itemQuantity,
            UnitPrice = itemTotal / itemQuantity,
            ItemTotal = itemTotal,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(OrderBillingCreditMigrationTests)
        });
        return order;
    }

    private static string FinancialJson(string currency, long claimedCreditMinor = 1_000) => $$"""
        {"currency":"{{currency}}","removedUnitValueMinor":{{claimedCreditMinor}},
         "potentialCreditMinor":{{claimedCreditMinor}},
         "addedAmountMinor":0,"netAccountDeltaMinor":-{{claimedCreditMinor}},"resolutionStatus":"Resolved",
         "creditState":"BalanceReduction","loyaltyState":"None","refundState":"None"}
        """;

    private async Task MigrateToAsync(string target)
    {
        await using var context = _fixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }

    private async Task MigrateToLatestAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.MigrateAsync();
    }

    private async Task<bool> RawRelationMissingAsync(string relation)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass(@relation) IS NULL", connection);
        command.Parameters.AddWithValue("relation", $"public.{relation}");
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<LegacyOrderSnapshot> ReadLegacyOrderAsync(Guid orderId)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT total, remaining_amount, payment_status FROM orders WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", orderId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new LegacyOrderSnapshot(reader.GetDecimal(0), reader.GetDecimal(1), reader.GetString(2));
    }

    private async Task<bool> FinancialJsonMatchesAsync(Guid amendmentId, string expectedJson)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT financial_resolution_json = CAST(@expected AS jsonb) FROM order_amendments WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("expected", expectedJson);
        command.Parameters.AddWithValue("id", amendmentId);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<LegacyTenderSnapshot> ReadTenderAsync(Guid orderId)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT payment_method, amount, status, transaction_id, currency FROM order_payments WHERE order_id = @id",
            connection);
        command.Parameters.AddWithValue("id", orderId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new LegacyTenderSnapshot(
            reader.GetString(0), reader.GetDecimal(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
    }

    private static Task<Order> ReadOrderAsync(ApplicationDbContext context, Guid orderId) =>
        context.Orders.IgnoreAutoIncludes().AsNoTracking().SingleAsync(value => value.Id == orderId);

    private sealed record CreditProbe(
        TableServiceSession Session,
        Order Order,
        OrderAmendment Amendment,
        string FinancialJson);

    private sealed record ProbeCorruption(
        bool EmptySourceSnapshot = false,
        bool EmptyChanges = false,
        Guid? SnapshotOrderId = null,
        decimal? SnapshotTotal = null,
        int? SnapshotItemQuantity = null,
        OrderAmendmentChangeKind ChangeKind = OrderAmendmentChangeKind.Void,
        int ChangeStartOrdinal = 1,
        bool DuplicateVoidRanges = false,
        long ClaimedCreditMinor = 1_000,
        decimal OrderDiscount = 0m,
        bool AddSecondCommittedAmendment = false);

    private sealed record LegacyOrderSnapshot(decimal Total, decimal RemainingAmount, string PaymentStatus);

    private sealed record LegacyTenderSnapshot(
        string Method,
        decimal Amount,
        string Status,
        string TransactionId,
        string? Currency);
}
