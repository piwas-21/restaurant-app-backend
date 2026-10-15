using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class CashierOrderArchiveTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly List<Guid> _todayOrderIds = [];
    private Guid _priorActiveOrderId;
    private Guid _priorUnpaidCompletedOrderId;
    private Guid _priorPaidCompletedOrderId;
    private Guid _priorCancelledUnpaidOrderId;
    private Guid _priorPaidDeliveredOrderId;
    private Guid _priorDismissedOrderId;
    private DateOnly _businessDate;

    [Fact]
    public async Task Archive_defaults_to_tenant_business_date_and_pages_current_and_unresolved_queues()
    {
        var currentPageOne = await ReadArchive(
            $"?search=ARCHIVE-TODAY&page=1&pageSize=1&priorDayUnresolvedPage=1&priorDayUnresolvedPageSize=1");
        var currentPageTwo = await ReadArchive(
            $"?businessDate={_businessDate:yyyy-MM-dd}&search=ARCHIVE-TODAY&page=2&pageSize=1"
            + "&priorDayUnresolvedPage=2&priorDayUnresolvedPageSize=1");

        currentPageOne.BusinessDate.Should().Be(_businessDate);
        currentPageOne.Orders.TotalCount.Should().Be(2);
        currentPageOne.Orders.TotalPages.Should().Be(2);
        currentPageOne.Orders.Items.Should().ContainSingle();
        currentPageTwo.Orders.Page.Should().Be(2);
        currentPageTwo.Orders.Items.Should().ContainSingle();
        currentPageOne.Orders.Items.Concat(currentPageTwo.Orders.Items)
            .Select(order => order.Id).Should().BeEquivalentTo(_todayOrderIds.ToArray());

        currentPageOne.PriorDayUnresolvedOrders.TotalCount.Should().Be(2);
        currentPageTwo.PriorDayUnresolvedOrders.TotalCount.Should().Be(2);
        currentPageOne.PriorDayUnresolvedOrders.Items.Concat(currentPageTwo.PriorDayUnresolvedOrders.Items)
            .Select(order => order.Id).Should().BeEquivalentTo([_priorActiveOrderId, _priorUnpaidCompletedOrderId]);
        var unresolvedIds = currentPageOne.PriorDayUnresolvedOrders.Items
            .Concat(currentPageTwo.PriorDayUnresolvedOrders.Items)
            .Select(order => order.Id).ToList();
        unresolvedIds.Should().NotContain(_priorPaidCompletedOrderId);
        unresolvedIds.Should().NotContain(_priorDismissedOrderId);
        unresolvedIds.Should().NotContain(_priorCancelledUnpaidOrderId);
        unresolvedIds.Should().NotContain(_priorPaidDeliveredOrderId);
    }

    [Fact]
    public async Task Prior_day_review_dismissal_is_durable_and_cashier_only()
    {
        AuthenticateAsRole(UserRole.Cashier);
        var first = await Client.PostAsync($"/api/orders/{_priorActiveOrderId}/prior-day-review/dismiss", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var repeated = await Client.PostAsync($"/api/orders/{_priorActiveOrderId}/prior-day-review/dismiss", null);
        repeated.StatusCode.Should().Be(HttpStatusCode.OK);

        var archive = await ReadArchive($"?businessDate={_businessDate:yyyy-MM-dd}&pageSize=100");
        archive.PriorDayUnresolvedOrders.Items.Select(order => order.Id)
            .Should().NotContain(_priorActiveOrderId);
        archive.PriorDayUnresolvedOrders.Items.Select(order => order.Id)
            .Should().Contain(_priorUnpaidCompletedOrderId);

        AuthenticateAsUser();
        var forbidden = await Client.PostAsync($"/api/orders/{_priorUnpaidCompletedOrderId}/prior-day-review/dismiss", null);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var archiveForbidden = await Client.GetAsync("/api/orders/cashier/archive");
        archiveForbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Current_day_order_cannot_be_dismissed_as_prior_day_review()
    {
        AuthenticateAsAdmin();
        var response = await Client.PostAsync($"/api/orders/{_todayOrderIds[0]}/prior-day-review/dismiss", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Short_numeric_search_matches_daily_number_without_matching_date_prefix()
    {
        var businessDate = new DateOnly(2026, 10, 15);
        var orderDate = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var displayFifteen = CreateOrder("ARCHIVE-DISPLAY-FIFTEEN", businessDate, 15,
            OrderStatus.Confirmed, PaymentStatus.Pending, orderDate, 10m, 0m);
        var displayTwo = CreateOrder("ARCHIVE-DISPLAY-TWO", businessDate, 2,
            OrderStatus.Confirmed, PaymentStatus.Pending, orderDate.AddMinutes(1), 12m, 0m);
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Orders.AddRange(displayFifteen, displayTwo);
            await context.SaveChangesAsync();
        }

        var plainFifteen = await ReadArchive(
            $"?businessDate={businessDate:yyyy-MM-dd}&search=15&pageSize=100");
        plainFifteen.Orders.TotalCount.Should().Be(1);
        plainFifteen.Orders.Items.Should().ContainSingle(order =>
            order.Id == displayFifteen.Id && order.DailyOrderNumber == 15);

        var prefixedFifteen = await ReadArchive(
            $"?businessDate={businessDate:yyyy-MM-dd}&search=%2315&pageSize=100");
        prefixedFifteen.Orders.Items.Should().ContainSingle(order => order.Id == displayFifteen.Id);

        var plainTwo = await ReadArchive(
            $"?businessDate={businessDate:yyyy-MM-dd}&search=2&pageSize=100");
        plainTwo.Orders.Items.Should().ContainSingle(order => order.Id == displayTwo.Id);

        var fullReference = await ReadArchive(
            $"?businessDate={businessDate:yyyy-MM-dd}&search={displayFifteen.OrderNumber}&pageSize=100");
        fullReference.Orders.Items.Should().ContainSingle(order => order.Id == displayFifteen.Id);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<ApplicationDbContext>();
        var clock = services.GetRequiredService<ITenantClock>();
        _businessDate = DateOnly.FromDateTime(clock.Now.Date);
        var priorDate = _businessDate.AddDays(-1);
        var nowUtc = clock.Now.UtcDateTime;

        var firstToday = CreateOrder("ARCHIVE-TODAY-FIRST", _businessDate, 9001,
            OrderStatus.Confirmed, PaymentStatus.Pending, nowUtc.AddMinutes(-2), 10m, 0m);
        firstToday.CustomerName = "ARCHIVE-TODAY first";
        var secondToday = CreateOrder("ARCHIVE-TODAY-SECOND", _businessDate, 9002,
            OrderStatus.Confirmed, PaymentStatus.Pending, nowUtc.AddMinutes(-1), 15m, 0m);
        secondToday.CustomerName = "ARCHIVE-TODAY second";
        _todayOrderIds.Add(firstToday.Id);
        _todayOrderIds.Add(secondToday.Id);

        var priorActive = CreateOrder("ARCHIVE-PRIOR-ACTIVE", priorDate, 9001,
            OrderStatus.Preparing, PaymentStatus.Pending, nowUtc.AddDays(-1), 12m, 0m);
        var priorCompletedUnpaid = CreateOrder("ARCHIVE-PRIOR-COMPLETED-UNPAID", priorDate, 9002,
            OrderStatus.Completed, PaymentStatus.Pending, nowUtc.AddDays(-1).AddMinutes(1), 20m, 0m);
        var priorCompletedPaid = CreateOrder("ARCHIVE-PRIOR-COMPLETED-PAID", priorDate, 9003,
            OrderStatus.Completed, PaymentStatus.Completed, nowUtc.AddDays(-1).AddMinutes(2), 30m, 30m);
        var priorDismissed = CreateOrder("ARCHIVE-PRIOR-DISMISSED", priorDate, 9004,
            OrderStatus.Preparing, PaymentStatus.Pending, nowUtc.AddDays(-1).AddMinutes(3), 8m, 0m);
        priorDismissed.PriorDayReviewDismissedAt = nowUtc;
        priorDismissed.PriorDayReviewDismissedBy = nameof(CashierOrderArchiveTests);
        var priorCancelledUnpaid = CreateOrder("ARCHIVE-PRIOR-CANCELLED-UNPAID", priorDate, 9005,
            OrderStatus.Cancelled, PaymentStatus.Pending, nowUtc.AddDays(-1).AddMinutes(4), 7m, 0m);
        var priorPaidDelivered = CreateOrder("ARCHIVE-PRIOR-PAID-DELIVERED", priorDate, 9006,
            OrderStatus.Delivered, PaymentStatus.Completed, nowUtc.AddDays(-1).AddMinutes(5), 9m, 9m);
        _priorActiveOrderId = priorActive.Id;
        _priorUnpaidCompletedOrderId = priorCompletedUnpaid.Id;
        _priorPaidCompletedOrderId = priorCompletedPaid.Id;
        _priorDismissedOrderId = priorDismissed.Id;
        _priorCancelledUnpaidOrderId = priorCancelledUnpaid.Id;
        _priorPaidDeliveredOrderId = priorPaidDelivered.Id;

        context.Orders.AddRange(firstToday, secondToday, priorActive, priorCompletedUnpaid,
            priorCompletedPaid, priorDismissed, priorCancelledUnpaid, priorPaidDelivered);
        await context.SaveChangesAsync();
    }

    private async Task<CashierOrderArchiveDto> ReadArchive(string query)
    {
        AuthenticateAsAdmin();
        var response = await Client.GetAsync("/api/orders/cashier/archive" + query);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var envelope = JsonSerializer.Deserialize<ApiResponse<CashierOrderArchiveDto>>(body, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue(body);
        return envelope.Data!;
    }

    private static Order CreateOrder(
        string label, DateOnly businessDate, long dailyNumber, OrderStatus status,
        PaymentStatus paymentStatus, DateTime orderDate, decimal total, decimal totalPaid) => new()
        {
            Id = Guid.NewGuid(),
            OrderNumber = businessDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
            + dailyNumber.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            BusinessDate = businessDate,
            DailyOrderNumber = dailyNumber,
            CustomerName = label,
            Type = OrderType.Takeaway,
            Status = status,
            PaymentStatus = paymentStatus,
            SubTotal = total,
            Total = total,
            TotalPaid = totalPaid,
            RemainingAmount = Math.Max(0m, total - totalPaid),
            OrderDate = orderDate,
            CreatedAt = orderDate,
            CreatedBy = nameof(CashierOrderArchiveTests),
        };
}
