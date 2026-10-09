using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.User.Commands.DeleteUserCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using Moq;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task Resolved_loyalty_refund_remains_readable_after_owner_erasure_and_rejects_posting_corruption()
    {
        _refundState.LoseFirstCreateResponse = false;
        var ownerId = Guid.NewGuid();
        await using (var removeOldAmendment = DatabaseFixture.CreateContext())
        {
            await TestUserSeeder.SeedUserAsync(removeOldAmendment, ownerId);
            var old = await removeOldAmendment.OrderAmendments.SingleAsync(value => value.Id == _amendmentId);
            removeOldAmendment.OrderAmendments.Remove(old);
            await removeOldAmendment.SaveChangesAsync();
        }

        await using (var accepted = DatabaseFixture.CreateContext())
        {
            var source = await accepted.Orders.Include(value => value.Items).Include(value => value.Payments)
                .SingleAsync(value => value.Id == _orderId);
            source.UserId = ownerId;
            source.FidelityPointsEarned = 20;
            await accepted.SaveChangesAsync();

            var snapshot = OrderBillingSnapshotFactory.Build(source, "CHF",
                new OrderBillingEarningEvaluation(20, "fixed-priority-v1", new string('d', 64),
                    new OrderBillingEarningRuleEvidence(Guid.NewGuid(), "Resolved clawback test rule",
                        0m, null, 20, 1)), null, 1_000);
            accepted.OrderBillingSnapshots.Add(snapshot.Header);
            accepted.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
            accepted.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);
            await accepted.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var awards = scope.ServiceProvider.GetRequiredService<IFidelityPointsService>();
            (await awards.AwardAcceptedOrderAsync(_orderId)).Disposition
                .Should().Be(FidelityPointsAwardDisposition.Awarded);
        }

        await using (var amendmentContext = DatabaseFixture.CreateContext())
        {
            var now = DateTime.UtcNow;
            var amendment = NewAmendment(now);
            var change = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(
                amendment.ChangesJson).Single();
            amendment.ChangesJson = OrderAmendmentJson.Serialize(new[] { change with
            {
                Quantity = 2,
                Previous = change.Previous with { Quantity = 2 }
            } });
            amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(
                new OrderAmendmentFinancialPreviewDto("CHF", 0, 2_000, -2_000, 2_000,
                    OrderAmendmentFinancialResolutionStatus.Pending,
                    OrderAmendmentCreditState.PendingAllocationReview,
                    OrderAmendmentLoyaltyState.PendingReview,
                    OrderAmendmentRefundState.GatewayRefundRequired));
            amendmentContext.OrderAmendments.Add(amendment);
            await amendmentContext.SaveChangesAsync();
        }

        AuthenticateAsAdmin();
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        using var contextResponse = await Client.GetAsync($"{path}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await contextResponse.Content.ReadAsStringAsync());
        var context = (await contextResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionContextDto>>(JsonOptions))!.Data!;
        var request = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = context.ExpectedOrderVersion,
            ExpectedAccountRevision = context.ExpectedAccountRevision,
            Currency = "CHF"
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{path}/quote", request, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await quoteResponse.Content.ReadAsStringAsync());
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.CreditMinor.Should().Be(2_000);
        quote.RefundMinor.Should().Be(2_000);
        quote.Loyalty!.EarnedClawbackPoints.Should().Be(20);
        quote.Loyalty.AwardPending.Should().BeFalse();
        quote.Loyalty.AppliedAwardPoints.Should().Be(20);

        using var startResponse = await Client.PostAsJsonAsync(path,
            new OrderAmendmentResolutionStartRequest
            {
                Quote = request,
                QuoteHash = quote.QuoteHash,
                ExpiresAt = quote.ExpiresAt
            }, JsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await startResponse.Content.ReadAsStringAsync());
        var start = (await startResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        start.Outcome.Should().Be("accepted");
        start.Result!.State.Should().Be("Resolved");
        start.Result.Loyalty!.EarnedClawbackPoints.Should().Be(20);
        start.Result.Loyalty.PostedClawbackPoints.Should().Be(20);
        _refundState.CreateCalls.Should().Be(1);
        _refundState.CreatedEvidence.Should().ContainSingle();
        _refundState.ProviderIoObservedDatabaseTransaction.Should().BeFalse();

        Guid postingId;
        await using (var preErasure = DatabaseFixture.CreateContext())
        {
            (await new AccountDebtSnapshotReader(preErasure).ReadAsync(_sessionId, CancellationToken.None))
                .Session.Currency.Should().Be("CHF");
            var compensation = await preErasure.OrderAmendmentLoyaltyCompensations
                .SingleAsync(value => value.OperationId == start.Result.OperationId);
            compensation.RequiredPoints.Should().Be(20);
            var reservation = await preErasure.OrderAmendmentLoyaltyReservations
                .SingleAsync(value => value.OperationId == start.Result.OperationId);
            reservation.State.Should().Be(OrderAmendmentLoyaltyReservationState.Consumed);
            var posting = await preErasure.OrderAmendmentLoyaltyCompensationPostings
                .SingleAsync(value => value.CompensationId == compensation.Id);
            posting.PointsDelta.Should().Be(-20);
            postingId = posting.Id;
        }

        await using (var erase = DatabaseFixture.CreateContext())
        {
            var deletion = new DeleteUserCommandHandler(erase, Mock.Of<ICurrentUserService>(),
                new RetainedCustomerDataScrubber(erase), NullLogger<DeleteUserCommandHandler>.Instance);
            var erased = await deletion.Handle(new DeleteUserCommand(ownerId, Permanent: true),
                CancellationToken.None);
            erased.Success.Should().BeTrue(erased.Message);
        }

        await using (var afterErasure = DatabaseFixture.CreateContext())
        {
            (await afterErasure.Orders.SingleAsync(value => value.Id == _orderId)).UserId.Should().BeNull();
            var ownerLink = await afterErasure.OrderBillingSnapshotOwnerLinks
                .SingleAsync(value => value.OrderId == _orderId && value.Slot == OrderBillingSnapshotOwnerSlot.Earning);
            ownerLink.Disposition.Should().Be(OrderBillingSnapshotOwnerDisposition.Erased);
            ownerLink.UserId.Should().BeNull();
            var earned = await afterErasure.FidelityPointsTransactions.AsNoTracking()
                .SingleAsync(value => value.OrderId == _orderId && value.TransactionType == TransactionType.Earned);
            earned.UserId.Should().BeNull();
            earned.Description.Should().Be("[erased]");
            (await new AccountDebtSnapshotReader(afterErasure).ReadAsync(_sessionId, CancellationToken.None))
                .Session.Currency.Should().Be("CHF");
        }

        await using (var corrupt = DatabaseFixture.CreateContext())
        await using (var transaction = await corrupt.Database.BeginTransactionAsync())
        {
            var changed = await corrupt.OrderAmendmentLoyaltyCompensationPostings
                .Where(value => value.Id == postingId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.PointsDelta,
                    value => value.PointsDelta + 1));
            changed.Should().Be(1);
            var read = () => new AccountDebtSnapshotReader(corrupt)
                .ReadAsync(_sessionId, CancellationToken.None);
            await read.Should().ThrowAsync<ConflictException>();
            await transaction.RollbackAsync();
        }

        await using (var restored = DatabaseFixture.CreateContext())
        {
            (await restored.OrderAmendmentLoyaltyCompensationPostings.AsNoTracking()
                .SingleAsync(value => value.Id == postingId)).PointsDelta.Should().Be(-20);
            (await new AccountDebtSnapshotReader(restored).ReadAsync(_sessionId, CancellationToken.None))
                .Session.Currency.Should().Be("CHF");
        }

        _refundState.CreateCalls.Should().Be(1, "the read and corruption probe cannot repeat provider refunds");
        _refundState.CreatedEvidence.Should().ContainSingle();
    }
}
