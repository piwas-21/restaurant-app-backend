using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task RetirementRejectsUnresolvedOperationOnAnotherOrderInTheSameSession()
    {
        await SeedUnevaluatedFullVoidAsync();
        await AddUnresolvedOperationForAnotherOrderAsync();

        using var response = await PostRetirementAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain(
            "Resolve the earlier paid correction in this table account first.");
        await AssertNoEarningRetirementAsync();
    }

    [Fact]
    public async Task RetirementRejectsPriorUnresolvedAmendmentOnTheSameOrder()
    {
        await SeedUnevaluatedFullVoidAsync();
        await AddPriorUnresolvedAmendmentAsync();

        using var response = await PostRetirementAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain(
            "Resolve the earlier committed amendment before starting another paid correction.");
        await AssertNoEarningRetirementAsync();
    }

    private async Task<HttpResponseMessage> PostRetirementAsync()
    {
        AuthenticateAsAdmin();
        await using var context = DatabaseFixture.CreateContext();
        var source = await context.Orders.AsNoTracking().Include(value => value.ServiceSession)
            .SingleAsync(value => value.Id == _orderId);
        return await Client.PostAsJsonAsync(
            $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution/prepare-earning-retirement",
            new OrderAmendmentEarningRetirementRequest
            {
                ExpectedOrderVersion = source.Version,
                ExpectedAccountRevision = source.ServiceSession!.AccountRevision
            }, JsonOptions);
    }

    private async Task AddUnresolvedOperationForAnotherOrderAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var now = DateTime.UtcNow;
        var otherOrderId = Guid.NewGuid();
        var otherAmendmentId = Guid.NewGuid();
        var otherOrder = new Order
        {
            Id = otherOrderId,
            OrderNumber = $"RET-{otherOrderId:N}"[..15],
            Type = OrderType.DineIn,
            ServiceSessionId = _sessionId,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.PartiallyPaid,
            SubTotal = 1m,
            Total = 1m,
            TotalPaid = 1m,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        };
        context.Orders.Add(otherOrder);
        context.OrderAmendments.Add(NewUnresolvedAmendment(otherOrderId, otherAmendmentId, now));
        context.OrderAmendmentResolutionOperations.Add(new OrderAmendmentResolutionOperation
        {
            Id = Guid.NewGuid(),
            AmendmentId = otherAmendmentId,
            SourceOrderId = otherOrderId,
            ServiceSessionId = _sessionId,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = AdminId,
            ActorRole = UserRole.Admin.ToString(),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            Currency = "CHF",
            CreditMinor = 100,
            RefundMinor = 100,
            UnpaidWaivedMinor = 0,
            RequestHash = new string('c', 64),
            SnapshotJson = "{}",
            State = OrderAmendmentResolutionOperationState.ReconciliationRequired,
            StartedAt = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        });
        await context.SaveChangesAsync();
    }

    private async Task AddPriorUnresolvedAmendmentAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        context.OrderAmendments.Add(NewUnresolvedAmendment(_orderId, Guid.NewGuid(), DateTime.UtcNow));
        await context.SaveChangesAsync();
    }

    private OrderAmendment NewUnresolvedAmendment(Guid sourceOrderId, Guid amendmentId, DateTime now) => new()
    {
        Id = amendmentId,
        SourceOrderId = sourceOrderId,
        ServiceSessionId = _sessionId,
        ActorUserId = AdminId,
        ActorRole = UserRole.Admin.ToString(),
        State = OrderAmendmentState.Committed,
        PayloadHash = new string('a', 64),
        CommitPayloadHash = new string('b', 64),
        ExpectedOrderVersion = 1,
        ExpectedAccountRevision = 1,
        CommittedAccountRevision = 2,
        ExpiresAt = now.AddMinutes(5),
        CommittedAt = now,
        RequestJson = "{}",
        ChangesJson = "[]",
        SourceSnapshotJson = "{}",
        FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto(
            "CHF", 0, 100, -100, 100, OrderAmendmentFinancialResolutionStatus.Pending,
            OrderAmendmentCreditState.PendingAllocationReview, OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.GatewayRefundRequired)),
        CreatedAt = now,
        CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
    };

    private async Task AssertNoEarningRetirementAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        (await context.OrderBillingEarningRetirements.AnyAsync(value => value.OrderId == _orderId))
            .Should().BeFalse();
        _refundState.CreateCalls.Should().Be(0);
    }
}
