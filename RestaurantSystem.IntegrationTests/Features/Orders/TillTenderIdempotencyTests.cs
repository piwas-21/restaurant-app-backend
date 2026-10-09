using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// A till tender is idempotent per the client's operation id (#523). Frontend #772 already
/// stops double-taps locally; this contract makes the RETRY safe — the shape a network
/// timeout actually produces: the first submit may have committed, the client cannot know,
/// and resends the SAME operation. The backend must answer that with the original result,
/// never a second tender.
/// <para>
/// Asserted over HTTP against real PostgreSQL (the applicator + the filtered unique index
/// are the subject), with the ledger read back through the DbContext after each call.
/// </para>
/// </summary>
[Collection("Database Lane 3")]
public class TillTenderIdempotencyTests : IntegrationTestBase
{
    private Guid _orderId;
    private Guid _otherOrderId;

    public TillTenderIdempotencyTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task A_duplicate_operation_returns_the_original_tender_and_never_banks_a_second_one()
    {
        AuthenticateAsAdmin();
        var operationId = Guid.NewGuid();

        var first = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 10m, tipMinor = 325L });
        first.IsSuccessStatusCode.Should().BeTrue();
        var firstBody = await ReadResponseAsync<ApiResponse<OrderDto>>(first);
        firstBody!.Data!.PaymentTipMinor.Should().Be(325);

        // The retry arrives AFTER the first submit committed (and completed the order) —
        // the timeout-after-commit shape. Eligibility no longer holds; idempotency must win.
        var retry = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 10m, tipMinor = 325L });
        retry.IsSuccessStatusCode.Should().BeTrue("the retry of a committed tender is a success, not a failure");
        var retryBody = await ReadResponseAsync<ApiResponse<OrderDto>>(retry);
        retryBody!.Success.Should().BeTrue();
        retryBody.Message.Should().Be("Payment already recorded",
            "the replay says so, so the UI can tell it from a fresh success");

        await using var context = DatabaseFixture.CreateContext();
        var tenders = await context.OrderPayments.AsNoTracking()
            .Where(p => p.OrderId == _orderId)
            .ToListAsync();
        tenders.Should().ContainSingle("the operation banked exactly one tender");
        tenders.Single().Amount.Should().Be(10m);
        tenders.Single().OperationId.Should().Be(operationId);

        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == _orderId);
        order.TotalPaid.Should().Be(10m, "the summary counted the tender once");
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);
    }

    [Fact]
    public async Task An_omitted_tip_replays_as_an_explicit_zero_tip()
    {
        AuthenticateAsAdmin();
        var operationId = Guid.NewGuid();

        var first = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 10m });
        first.IsSuccessStatusCode.Should().BeTrue();

        var replay = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 10m, tipMinor = 0L });
        replay.IsSuccessStatusCode.Should().BeTrue();
        var replayBody = await ReadResponseAsync<ApiResponse<OrderDto>>(replay);
        replayBody!.Success.Should().BeTrue("an omitted tip has the same zero-value meaning as explicit zero");
        replayBody.Message.Should().Be("Payment already recorded");

        await using var context = DatabaseFixture.CreateContext();
        var tenders = await context.OrderPayments.AsNoTracking()
            .Where(payment => payment.OrderId == _orderId)
            .ToListAsync();
        tenders.Should().ContainSingle();
        tenders.Single().TipMinor.Should().Be(0);
    }

    [Fact]
    public async Task A_repeated_operation_with_a_different_payload_is_refused_and_banks_nothing()
    {
        AuthenticateAsAdmin();
        var operationId = Guid.NewGuid();

        var first = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 10m, tipMinor = 325L });
        first.IsSuccessStatusCode.Should().BeTrue();

        // Same operation id, different METHOD.
        var byCard = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "CreditCard", amount = 10m, tipMinor = 325L });
        byCard.IsSuccessStatusCode.Should().BeTrue("business refusals ride the response body");
        var cardBody = await ReadResponseAsync<ApiResponse<OrderDto>>(byCard);
        cardBody!.Success.Should().BeFalse();
        cardBody.ErrorCode.Should().Be(ErrorCodes.PaymentOperationPayloadMismatch,
            "the frontend renders this refusal, not a generic failure");

        // Same operation id, different AMOUNT.
        var forLess = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 4m, tipMinor = 325L });
        var lessBody = await ReadResponseAsync<ApiResponse<OrderDto>>(forLess);
        lessBody!.Success.Should().BeFalse();
        lessBody.ErrorCode.Should().Be(ErrorCodes.PaymentOperationPayloadMismatch);

        var withDifferentTip = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 10m, tipMinor = 326L });
        var tipBody = await ReadResponseAsync<ApiResponse<OrderDto>>(withDifferentTip);
        tipBody!.Success.Should().BeFalse();
        tipBody.ErrorCode.Should().Be(ErrorCodes.PaymentOperationPayloadMismatch,
            "a retry may not change the tip after the original tender is committed");

        await using var context = DatabaseFixture.CreateContext();
        var tenders = await context.OrderPayments.AsNoTracking()
            .Where(p => p.OrderId == _orderId)
            .ToListAsync();
        tenders.Should().ContainSingle("neither mismatched retry may bank a tender");
        tenders.Single().PaymentMethod.Should().Be(PaymentMethod.Cash, "the original tender stands untouched");
        tenders.Single().TipMinor.Should().Be(325);

        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == _orderId);
        order.TotalPaid.Should().Be(10m, "cashier gratuity must not inflate food debt settlement");
    }

    [Fact]
    public async Task A_negative_tip_is_rejected_without_banking_a_payment()
    {
        AuthenticateAsAdmin();
        var response = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId = Guid.NewGuid(), paymentMethod = "Cash", amount = 10m, tipMinor = -1 });
        var excessiveTip = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new
            {
                operationId = Guid.NewGuid(),
                paymentMethod = "Cash",
                amount = 10m,
                tipMinor = OrderPayment.MaximumTipMinor + 1
            });

        response.IsSuccessStatusCode.Should().BeFalse("minor-unit tips are non-negative exact amounts");
        excessiveTip.IsSuccessStatusCode.Should().BeFalse("tip input keeps the supported upper bound");
        await using var context = DatabaseFixture.CreateContext();
        (await context.OrderPayments.AsNoTracking().CountAsync(p => p.OrderId == _orderId)).Should().Be(0);
    }

    [Fact]
    public async Task An_operation_id_already_used_on_another_order_is_refused()
    {
        AuthenticateAsAdmin();
        var operationId = Guid.NewGuid();

        var first = await Client.PostAsJsonAsync(
            $"/api/Orders/{_orderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 10m });
        first.IsSuccessStatusCode.Should().BeTrue();

        var second = await Client.PostAsJsonAsync(
            $"/api/Orders/{_otherOrderId}/payments",
            new { operationId, paymentMethod = "Cash", amount = 10m });
        second.IsSuccessStatusCode.Should().BeTrue();
        var body = await ReadResponseAsync<ApiResponse<OrderDto>>(second);
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.PaymentOperationIdReused,
            "one operation id is one cashier action on one order");

        await using var context = DatabaseFixture.CreateContext();
        var tendersOnOther = await context.OrderPayments.AsNoTracking()
            .Where(p => p.OrderId == _otherOrderId)
            .ToListAsync();
        tendersOnOther.Should().BeEmpty("a reused operation id must not bank a tender on the second order");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = new Order
        {
            OrderNumber = "C04-IDEM",
            CustomerName = "Walk-in",
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = DateTime.UtcNow,
            Total = 10m,
            CreatedBy = "test",
        };
        var other = new Order
        {
            OrderNumber = "C04-IDEM-2",
            CustomerName = "Walk-in",
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = DateTime.UtcNow,
            Total = 10m,
            CreatedBy = "test",
        };

        context.AddRange(order, other);
        await context.SaveChangesAsync();
        _orderId = order.Id;
        _otherOrderId = other.Id;
    }
}
