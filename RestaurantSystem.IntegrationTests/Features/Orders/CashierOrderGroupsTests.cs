using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Infrastructure.Persistence.Migrations;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>Real HTTP/PostgreSQL membership, filtering and group pagination controls.</summary>
[Collection("Database Lane 2")]
public sealed class CashierOrderGroupsTests(DatabaseFixture databaseFixture) : IntegrationTestBase(databaseFixture)
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<TenantFeatureSettings>(settings => settings.OrderAmendmentsV1 = true);

    private static readonly DateTime Opened = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private Guid _openSessionId;
    private Guid _closedSessionId;
    private readonly List<Guid> _roundIds = [];

    [Fact]
    public async Task Page_size_counts_whole_visits_and_keeps_more_than_fifty_rounds_together()
    {
        var first = await Read("search=GRP-&pageSize=1&page=1");
        var second = await Read("search=GRP-&pageSize=1&page=2");
        first.TotalCount.Should().Be(2);
        first.TotalPages.Should().Be(2);
        var groups = first.Items.Concat(second.Items).ToList();
        groups.Select(group => group.GroupKey).Should().OnlyHaveUniqueItems();
        var visit = groups.Single(group => group.ServiceSessionId == _openSessionId);
        visit.Orders.Should().HaveCount(55);
        visit.Orders.Select(order => order.Id).Should().BeEquivalentTo(_roundIds);
        visit.Orders.Should().BeInAscendingOrder(order => order.OrderDate);
        visit.Orders.Should().OnlyContain(order => order.PermittedActions != null);
        groups.Should().OnlyContain(group => group.ServiceSessionId.HasValue);
    }

    [Fact]
    public async Task One_matching_child_selects_the_complete_visit_including_cancelled_rounds()
    {
        var result = await Read("scope=Operational&search=GRP-MATCH&status=Confirmed&pageSize=10");
        result.Items.Should().ContainSingle();
        var group = result.Items.Single();
        group.ServiceSessionId.Should().Be(_openSessionId);
        group.Orders.Select(order => order.Id).Should().BeEquivalentTo(_roundIds);
        group.Orders.Should().Contain(order => order.Status == "Cancelled");
        group.TableNumber.Should().Be(700);
    }

    [Fact]
    public async Task Current_day_group_filter_keeps_prior_day_visit_balance_in_full_membership()
    {
        const string priorOrderNumber = "202602010001";
        const string currentOrderNumber = "202602020001";
        var priorDate = new DateOnly(2026, 2, 1);
        var currentDate = new DateOnly(2026, 2, 2);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 799,
            Currency = "CHF",
            OpenedAt = new DateTime(2026, 2, 1, 11, 0, 0, DateTimeKind.Utc),
            CreatedBy = nameof(CashierOrderGroupsTests)
        };
        context.TableServiceSessions.Add(session);
        var priorOrder = NewOrder(priorOrderNumber, session, 0);
        priorOrder.OrderDate = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        priorOrder.CreatedAt = priorOrder.OrderDate;
        priorOrder.Status = OrderStatus.Completed;
        priorOrder.Total = 10m;
        priorOrder.TotalPaid = 0m;
        priorOrder.RemainingAmount = 10m;
        var currentOrder = NewOrder(currentOrderNumber, session, 1);
        currentOrder.OrderDate = new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc);
        currentOrder.CreatedAt = currentOrder.OrderDate;
        currentOrder.Total = 5m;
        currentOrder.TotalPaid = 0m;
        currentOrder.RemainingAmount = 5m;
        context.Orders.AddRange(priorOrder, currentOrder);
        await context.SaveChangesAsync();

        var result = await Read(
            $"scope=Operational&tenantDay={currentDate:yyyy-MM-dd}&search={currentOrderNumber}&pageSize=10");

        result.Items.Should().ContainSingle();
        var visit = result.Items.Single();
        visit.ServiceSessionId.Should().Be(session.Id);
        visit.Orders.Select(order => order.OrderNumber)
            .Should().Equal(priorOrderNumber, currentOrderNumber);
        visit.Orders.Single(order => order.OrderNumber == priorOrderNumber).BusinessDate.Should().Be(priorDate);
        visit.Orders.Single(order => order.OrderNumber == priorOrderNumber).RemainingAmount.Should().Be(10m);
        visit.Orders.Sum(order => order.RemainingAmount).Should().Be(15m,
            "the date filter chooses which visit is visible, not which unpaid rounds contribute to its balance");
    }

    [Fact]
    public async Task Closed_visit_children_keep_original_order_identity_in_one_row()
    {
        var result = await Read("search=GRP-CLOSED&pageSize=10");
        result.TotalCount.Should().Be(1);
        result.Items.Should().OnlyContain(group => group.ServiceSessionId == _closedSessionId && group.Orders.Count == 2);
        result.Items.SelectMany(group => group.Orders).Should()
            .OnlyContain(order => order.ServiceSessionId == _closedSessionId);
        result.Items.SelectMany(group => group.Orders).Select(order => order.OrderNumber)
            .Should().BeEquivalentTo("GRP-CLOSED-ONE", "GRP-CLOSED-TWO");
    }

    [Fact]
    public async Task Out_of_range_page_returns_current_last_page_and_stable_ordering()
    {
        var last = await Read("search=GRP-&pageSize=1&page=99");
        var repeated = await Read("search=GRP-&pageSize=1&page=2");
        last.Page.Should().Be(2);
        last.Items.Select(group => group.GroupKey).Should().Equal(repeated.Items.Select(group => group.GroupKey));
    }

    [Theory]
    [InlineData(OrderType.DineIn)]
    [InlineData(OrderType.Takeaway)]
    [InlineData(OrderType.Delivery)]
    public async Task Historical_supplements_and_nested_updates_backfill_to_one_standalone_row(OrderType type)
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = await context.TableServiceSessions.SingleAsync(value => value.Id == _openSessionId);
        var original = NewOrder("ANCESTRY-ROOT", session, 80);
        var supplement = NewOrder("ANCESTRY-SECOND", session, 81);
        var nested = NewOrder("ANCESTRY-THIRD", session, 82);
        var unrelated = NewOrder("ANCESTRY-UNRELATED", session, 83);
        foreach (var order in new[] { original, supplement, nested, unrelated })
        {
            order.ServiceSessionId = null;
            order.Type = type;
        }
        context.Orders.AddRange(original, supplement, nested, unrelated);
        context.OrderAmendments.AddRange(
            HistoricalAmendment(original.Id, supplement.Id), HistoricalAmendment(supplement.Id, nested.Id));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync(StaffOrderSimplification.BackfillSql);
        await context.Database.ExecuteSqlRawAsync(StaffOrderSimplification.BackfillSql);
        var roots = await context.Orders.AsNoTracking().Where(order => order.OrderNumber.StartsWith("ANCESTRY-"))
            .ToDictionaryAsync(order => order.Id, order => order.AmendmentRootOrderId);
        roots[original.Id].Should().BeNull();
        roots[unrelated.Id].Should().BeNull();
        roots[supplement.Id].Should().Be(original.Id);
        roots[nested.Id].Should().Be(original.Id);
        var result = await Read("search=ANCESTRY-THIRD&pageSize=1");
        result.TotalCount.Should().Be(1);
        result.Items.Single().Orders.Select(order => order.Id).Should().Equal(original.Id, supplement.Id, nested.Id);
        result.Items.Single().HasUpdates.Should().BeTrue();
    }

    [Fact]
    public async Task Amendment_without_additions_marks_the_original_row_updated()
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = await context.TableServiceSessions.SingleAsync(value => value.Id == _openSessionId);
        var source = NewOrder("AMENDED-NO-ADD", session, 90);
        source.ServiceSessionId = null;
        source.Type = OrderType.Takeaway;
        context.Orders.Add(source);
        context.OrderAmendments.Add(HistoricalAmendment(source.Id, null));
        await context.SaveChangesAsync();
        var result = await Read("search=AMENDED-NO-ADD");
        result.Items.Should().ContainSingle();
        result.Items.Single().HasUpdates.Should().BeTrue();
        result.Items.Single().Orders.Single().Id.Should().Be(source.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Group_history_reads_all_members_in_one_request_without_unrelated_orders(bool visit)
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = await context.TableServiceSessions.SingleAsync(value => value.Id == _openSessionId);
        var root = NewOrder("HISTORY-ROOT", session, 91);
        var child = NewOrder("HISTORY-CHILD", session, 92);
        var unrelated = NewOrder("HISTORY-OTHER", session, 93);
        child.AmendmentRootOrderId = root.Id;
        if (!visit)
        {
            root.ServiceSessionId = null;
            child.ServiceSessionId = null;
        }
        unrelated.ServiceSessionId = _closedSessionId;
        context.Orders.AddRange(root, child, unrelated);
        context.OrderAmendments.AddRange(HistoricalAmendment(root.Id, null),
            HistoricalAmendment(child.Id, null), HistoricalAmendment(unrelated.Id, null));
        await context.SaveChangesAsync();
        AuthenticateAsAdmin();
        foreach (var selected in new[] { root.Id, child.Id })
        {
            var response = await Client.GetAsync($"/api/staff/orders/{selected}/amendments?includeGroup=true");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var history = await ReadResponseAsync<ApiResponse<List<OrderAmendmentHistoryDto>>>(response);
            history!.Data!.Select(value => value.SourceOrderId).Should().BeEquivalentTo(new[] { root.Id, child.Id });
        }
        var singleResponse = await Client.GetAsync($"/api/staff/orders/{root.Id}/amendments");
        singleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var single = await ReadResponseAsync<ApiResponse<List<OrderAmendmentHistoryDto>>>(singleResponse);
        single!.Data!.Should().ContainSingle().Which.SourceOrderId.Should().Be(root.Id);
        AuthenticateAsUser();
        var denied = await Client.GetAsync($"/api/staff/orders/{root.Id}/amendments?includeGroup=true");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static OrderAmendment HistoricalAmendment(Guid source, Guid? supplement) => new()
    {
        SourceOrderId = source,
        SupplementOrderId = supplement,
        State = OrderAmendmentState.Committed,
        ActorUserId = Guid.NewGuid(),
        ActorRole = "Cashier",
        PayloadHash = new string('A', 64),
        RequestJson = "{}",
        SourceSnapshotJson = "{}",
        FinancialResolutionJson = "{}",
        ExpiresAt = Opened,
        CreatedBy = nameof(CashierOrderGroupsTests),
    };

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("syncCursor=unknown")]
    [InlineData("modifiedSince=2026-01-01T00%3A00%3A00Z")]
    public async Task Incomplete_or_invalid_group_requests_are_refused(string query)
    {
        AuthenticateAsAdmin();
        var response = await Client.GetAsync($"/api/orders/cashier-groups?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Customer_cannot_read_the_staff_group_feed()
    {
        AuthenticateAsUser();
        var response = await Client.GetAsync("/api/orders/cashier-groups");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Released_visits_and_archived_legacy_debt_remain_visible_without_claiming_current_occupancy()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await context.TableServiceSessions.SingleAsync(value => value.Id == _openSessionId);
        session.ReleasedAt = Opened.AddHours(2);
        var table = new Table
        {
            Id = Guid.NewGuid(),
            TableNumber = "702",
            MaxGuests = 4,
            CreatedBy = nameof(CashierOrderGroupsTests)
        };
        var legacy = NewOrder("GRP-ARCHIVED", session, 70);
        legacy.ServiceSessionId = null;
        legacy.TableNumber = 702;
        legacy.Status = OrderStatus.Preparing;
        var operation = new TableOccupancyRecoveryOperation
        {
            Id = Guid.NewGuid(),
            TableId = table.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRole = UserRole.Admin,
            RequestHash = new string('a', 64),
            PreviewFingerprint = new string('b', 64),
            Reason = "Test recovery",
            ExpectedReadinessVersion = 1,
            OutcomeReadinessVersion = 2,
            OutcomeReadinessState = TableReadinessState.NeedsReset,
            RecordedAt = Opened.AddHours(2),
            CreatedBy = nameof(CashierOrderGroupsTests),
        };
        context.Tables.Add(table);
        context.Orders.Add(legacy);
        context.TableOccupancyRecoveryOperations.Add(operation);
        context.TableOccupancyRecoveryDispositions.Add(new TableOccupancyRecoveryDisposition
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            TableId = table.Id,
            OrderId = legacy.Id,
            OrderNumber = legacy.OrderNumber,
            WasLegacyUnassigned = true,
            Kind = TableOccupancyRecoveryDispositionKind.ArchivedLegacyOccupancy,
            OriginalStatus = legacy.Status,
            OriginalPaymentStatus = legacy.PaymentStatus,
            OriginalTotal = 10m,
            OriginalRemainingAmount = 10m,
            RecordedAt = operation.RecordedAt,
            CreatedBy = nameof(CashierOrderGroupsTests),
        });
        await context.SaveChangesAsync();
        var result = await Read("scope=Operational&search=GRP-&pageSize=10");
        var visit = result.Items.Single(group => group.ServiceSessionId == _openSessionId);
        visit.ReleasedAt.Should().Be(session.ReleasedAt);
        visit.IsArchivedFromTable.Should().BeFalse();
        var archived = result.Items.Single(group => group.Orders.Any(order => order.Id == legacy.Id));
        archived.ServiceSessionId.Should().BeNull();
        archived.IsArchivedFromTable.Should().BeTrue();
        archived.Orders.Single().RemainingAmount.Should().Be(10m);
        archived.Orders.Single().Status.Should().Be("Preparing");
        result.Items.Where(group => group.Orders.Any(order =>
            order.OrderNumber.StartsWith("GRP-CLOSED", StringComparison.Ordinal)))
            .Should().OnlyContain(group => !group.IsArchivedFromTable && group.ReleasedAt == null);
    }

    [Fact]
    public async Task Reading_groups_does_not_mutate_orders_or_visit_membership()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = await context.Orders.AsNoTracking().Where(order => _roundIds.Contains(order.Id))
            .OrderBy(order => order.Id).Select(order => new
            {
                order.Id,
                order.ServiceSessionId,
                order.OrderNumber,
                order.Status,
                order.PaymentStatus,
                order.Total,
                order.TotalPaid,
                order.Version
            })
            .ToListAsync();
        await Read("search=GRP-MATCH");
        var after = await context.Orders.AsNoTracking().Where(order => _roundIds.Contains(order.Id))
            .OrderBy(order => order.Id).Select(order => new
            {
                order.Id,
                order.ServiceSessionId,
                order.OrderNumber,
                order.Status,
                order.PaymentStatus,
                order.Total,
                order.TotalPaid,
                order.Version
            })
            .ToListAsync();
        after.Should().Equal(before);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var open = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 700,
            Currency = "CHF",
            OpenedAt = Opened,
            CreatedAt = Opened,
            CreatedBy = nameof(CashierOrderGroupsTests)
        };
        var closed = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 701,
            Currency = "CHF",
            Status = TableServiceSessionStatus.Closed,
            OpenedAt = Opened,
            ClosedAt = Opened.AddDays(1),
            CreatedAt = Opened,
            CreatedBy = nameof(CashierOrderGroupsTests)
        };
        _openSessionId = open.Id;
        _closedSessionId = closed.Id;
        context.TableServiceSessions.AddRange(open, closed);
        for (var index = 0; index < 55; index++)
        {
            var order = NewOrder(index == 54 ? "GRP-MATCH" : $"GRP-ROUND-{index:00}", open, index);
            if (index == 0) order.Status = OrderStatus.Cancelled;
            _roundIds.Add(order.Id);
            context.Orders.Add(order);
        }
        context.Orders.AddRange(NewOrder("GRP-CLOSED-ONE", closed, 60), NewOrder("GRP-CLOSED-TWO", closed, 61));
        await context.SaveChangesAsync();
    }

    private static Order NewOrder(string number, TableServiceSession session, int minute) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = number,
        ServiceSessionId = session.Id,
        TableNumber = session.TableNumber,
        Type = OrderType.DineIn,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Pending,
        Total = 10m,
        SubTotal = 10m,
        RemainingAmount = 10m,
        OrderDate = Opened.AddMinutes(minute),
        CreatedAt = Opened.AddMinutes(minute),
        CreatedBy = nameof(CashierOrderGroupsTests),
    };

    private async Task<PagedResult<CashierOrderGroupDto>> Read(string query)
    {
        AuthenticateAsAdmin();
        var response = await Client.GetAsync($"/api/orders/cashier-groups?{query}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var envelope = JsonSerializer.Deserialize<ApiResponse<PagedResult<CashierOrderGroupDto>>>(body, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue(body);
        return envelope.Data!;
    }
}
