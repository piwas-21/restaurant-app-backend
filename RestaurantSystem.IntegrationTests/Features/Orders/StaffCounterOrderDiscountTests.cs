using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class StaffCounterOrderTests
{
    [Fact]
    public async Task New_discounted_round_refuses_a_legacy_visit_without_loading_its_navigation()
    {
        var sessionId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var info = await context.RestaurantInfo.FirstAsync();
            info.Currency = "CHF";
            info.BillChargeRulesJson = JsonSerializer.Serialize(new[] { new BillChargeRuleDto(
                "discount", "Discount", true, 2m, true, ["DineIn"], ["Cashier", "Server"], "Reduce") }, BillChargeRules.JsonOptions);
            context.Tables.Add(new Table { Id = Guid.NewGuid(), TableNumber = "9", MaxGuests = 4, IsActive = true, CreatedBy = "test" });
            context.TableServiceSessions.Add(new TableServiceSession
            {
                Id = sessionId,
                TableNumber = 9,
                Currency = "CHF",
                Status = TableServiceSessionStatus.Open,
                BillingAllocationVersion = 0,
                OpenedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }
        AuthenticateAsAdmin();
        var response = await Client.PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "DineIn",
            tableNumber = 9,
            serviceSessionId = sessionId,
            paymentState = "PayLater",
            items = new[] { new { productId = _productId, quantity = 1 } },
            billCharges = new[] { new BillChargeInputDto("discount", 2m, "Reduce", "Fixed") }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.CountAsync()).Should().Be(0);
        (await verify.StaffOrderOperations.CountAsync()).Should().Be(0);
    }
}
