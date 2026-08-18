using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The staff till endpoint must not be able to mint a tender that can never be refunded.
///
/// <para>
/// Since S11, <c>OrderPayment.PaymentGateway</c> is not just a note — <see cref="TenderCustody"/>
/// reads it to decide whether a refund is even possible, and the refusal is deliberately not
/// overridable. <c>AddPaymentToOrderCommand</c> used to copy that field verbatim out of the request
/// body, so a staff API caller could set it on a cash payment and lock that money out of the refund
/// path permanently. It is the same defect #328 fixed on the anonymous order path, one endpoint
/// later, and it only became dangerous when the field started carrying meaning.
/// </para>
/// <para>
/// Asserted over HTTP with the field present in the JSON, not by calling the handler. The property
/// is gone from the command, so a handler-level test could not even express the attack; this one
/// stays meaningful if anyone re-adds it, because the body would then bind again and the assertion
/// would go red.
/// </para>
/// </summary>
[Collection("Database Lane 3")]
public class TillTenderCustodyTests : IntegrationTestBase
{
    private Guid _orderId;

    public TillTenderCustodyTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task A_till_payment_cannot_declare_a_gateway_and_stays_refundable()
    {
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new
            {
                paymentMethod = "Cash",
                amount = 10.0m,
                paymentGateway = "Stripe",
                paymentNotes = "cash in the drawer",
            });

        response.IsSuccessStatusCode.Should().BeTrue();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tender = await context.OrderPayments.SingleAsync(p => p.OrderId == _orderId);

        tender.PaymentGateway.Should().BeNull("only the Stripe settle path has actually seen a gateway");
        TenderCustody.IsHeldByGateway(tender).Should()
            .BeFalse("cash in a drawer must stay refundable however the request described it");

        // The benign field beside it still lands, so this pins a targeted refusal rather than a
        // body that stopped binding altogether.
        tender.PaymentNotes.Should().Be("cash in the drawer");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = new Order
        {
            OrderNumber = "S11-TILL",
            CustomerName = "Walk-in",
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = DateTime.UtcNow,
            Total = 10m,
            CreatedBy = "test",
        };

        context.Add(order);
        await context.SaveChangesAsync();
        _orderId = order.Id;
    }
}
