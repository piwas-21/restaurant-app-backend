using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class BillDiscountHistoryBudgetTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly Guid _id = Guid.NewGuid();
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<CurrentStaffTicketSettings>(settings => settings.MaximumAmendments = 1);

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var info = await context.RestaurantInfo.FirstAsync();
        info.Currency = "EUR";
        info.BillChargeRulesJson = JsonSerializer.Serialize(new[] {
            new BillChargeRuleDto("discount", "Discount", true, 1m, true, ["Takeaway"], ["Cashier"], "Reduce", "Fixed")
        }, BillChargeRules.JsonOptions);
        context.Orders.Add(new Order
        {
            Id = _id,
            OrderNumber = "BUDGET",
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            Total = 10m,
            SubTotal = 10m,
            RemainingAmount = 10m,
            OrderDate = DateTime.UtcNow,
            CreatedBy = "test",
            Items = [new OrderItem { Id = Guid.NewGuid(), OrderId = _id, Quantity = 1,
                ProductName = "Item", ItemTotal = 10m, CreatedBy = "test" }]
        });
        for (var index = 0; index < 2; index++)
            context.OrderAmendments.Add(new OrderAmendment
            {
                SourceOrderId = _id,
                State = OrderAmendmentState.Committed,
                ActorUserId = Guid.NewGuid(),
                ActorRole = "Cashier",
                PayloadHash = new string('A', 64),
                RequestJson = "{}",
                SourceSnapshotJson = "{}",
                FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto("EUR", 0, 0, 0, 0,
                    OrderAmendmentFinancialResolutionStatus.NotRequired, OrderAmendmentCreditState.None,
                    OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None)),
                ChangesJson = "[]",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedBy = "test"
            });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Discount_accepts_complete_history_exactly_at_the_configured_limit()
    {
        AuthenticateAsAdmin();
        await using var context = DatabaseFixture.CreateContext();
        var amendments = await context.OrderAmendments.Where(value => value.SourceOrderId == _id).ToListAsync();
        context.OrderAmendments.Remove(amendments[0]);
        await context.SaveChangesAsync();
        var order = await context.Orders.AsNoTracking().SingleAsync(value => value.Id == _id);
        var response = await Client.PostAsJsonAsync($"/api/bill-charges/orders/{_id}",
            new AddBillChargesRequest(Guid.NewGuid(), order.Version, [new("discount", 1m, "Reduce", "Fixed")]), JsonOptions);
        response.EnsureSuccessStatusCode();
        var after = await context.Orders.AsNoTracking().SingleAsync(value => value.Id == _id);
        after.BillChargeAmount.Should().Be(-1m);
        after.RemainingAmount.Should().Be(9m);
    }

    [Fact]
    public async Task Discount_refuses_partial_history_and_leaves_financial_evidence_unchanged()
    {
        AuthenticateAsAdmin();
        await using var context = DatabaseFixture.CreateContext();
        var before = await context.Orders.AsNoTracking().SingleAsync(value => value.Id == _id);
        var response = await Client.PostAsJsonAsync($"/api/bill-charges/orders/{_id}",
            new AddBillChargesRequest(Guid.NewGuid(), before.Version, [new("discount", 1m, "Reduce", "Fixed")]), JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("interactive amendment history limit");
        var after = await context.Orders.AsNoTracking().SingleAsync(value => value.Id == _id);
        after.BillChargesJson.Should().Be(before.BillChargesJson);
        after.BillChargeAmount.Should().Be(0m);
        after.RemainingAmount.Should().Be(10m);
        after.Version.Should().Be(before.Version);
    }
}
