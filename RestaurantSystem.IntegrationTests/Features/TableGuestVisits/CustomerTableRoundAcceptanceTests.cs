using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTableRoundAcceptanceTests(DatabaseFixture fixture) : CustomerTableRoundFixture(fixture)
{
    [Fact]
    public async Task Preparation_reviews_shared_table_prices_without_an_order_receipt_tender_or_basket_change()
    {
        var intent = await RoundIntentAsync();
        var basket = await ReadAsync();
        var prepared = await PrepareRoundAsync(intent);
        prepared.Quote.Money.SubTotal.Should().Be(14m);
        prepared.Quote.Money.Total.Should().Be(14m);
        prepared.Quote.Money.Tip.Should().Be(0m);
        prepared.Quote.Money.PointsRedeemed.Should().Be(0);
        prepared.Quote.AvailablePaymentMethods.Should().BeEmpty();
        prepared.TableLabel.Should().Be("Garden");
        (await ReadAsync()).Should().BeEquivalentTo(basket);
        await AssertNoFinancialEffectsAsync();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
        (await context.Set<TableGuestRoundOperation>().CountAsync()).Should().Be(0);
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
    }
    [Fact]
    public async Task Exact_submission_commits_one_unpaid_visit_round_with_both_receipts_and_replays_without_new_effects()
    {
        var intent = await RoundIntentAsync();
        var prepared = await PrepareRoundAsync(intent);
        var submitted = Submission(intent, prepared);
        var result = await RoundResolutionAsync(await SendAsync(RoundPath + "submit", submitted));
        result.Outcome.Should().Be(CustomerOrderOperationOutcome.Applied);
        result.OperationId.Should().Be(intent.OperationId);
        result.ServiceSessionId.Should().Be(Visit.ServiceSessionId);
        result.OrderId.Should().NotBeNull();
        var acceptedId = result.OrderId.GetValueOrDefault();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.Include(value => value.Items).SingleAsync();
            order.Id.Should().Be(acceptedId);
            order.ServiceSessionId.Should().Be(Visit.ServiceSessionId);
            order.TableId.Should().Be(TableId);
            order.UserId.Should().BeNull();
            order.Total.Should().Be(14m);
            order.TotalPaid.Should().Be(0m);
            order.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
            (await context.OrderPayments.CountAsync()).Should().Be(0);
            (await context.FidelityPointsTransactions.CountAsync()).Should().Be(0);
            var native = await context.Set<CustomerOrderOperation>().SingleAsync();
            native.OrderId.Should().Be(result.OrderId);
            native.PayloadHash.Should().Be(prepared.Proof.PayloadHash);
            var round = await context.Set<TableGuestRoundOperation>().SingleAsync();
            round.OrderId.Should().Be(acceptedId);
            round.OperationId.Should().NotBe(intent.OperationId);
            var visit = await context.TableServiceSessions.SingleAsync();
            visit.AccountRevision.Should().BeGreaterThan(intent.ExpectedAccountRevision);
            visit.Status = TableServiceSessionStatus.Closed;
            visit.ClosedAt = DateTime.UtcNow;
            visit.ReleasedAt = DateTime.UtcNow;
            (await context.TableGuestParticipants.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }
        WithdrawNative();
        foreach (var response in new[]
        {
            await ResolveRoundAsync(prepared), await ResolveRoundAsync(prepared, true),
            await SendAsync(RoundPath + "submit", submitted)
        })
            (await RoundResolutionAsync(response)).Should().Be(result);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.CountAsync()).Should().Be(1);
        (await verify.Set<CustomerOrderOperation>().CountAsync()).Should().Be(1);
        Notifications.Verify(value => value.CompleteAsync(It.Is<Order>(order => order.Id == result.OrderId), false,
            It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task Original_stopping_seals_only_an_unseen_round_and_prevents_late_submission_or_operation_reuse()
    {
        var intent = await RoundIntentAsync();
        var prepared = await PrepareRoundAsync(intent);
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
        WithdrawNative();
        var stopped = await RoundResolutionAsync(await ResolveRoundAsync(prepared, true));
        stopped.Outcome.Should().Be(CustomerOrderOperationOutcome.Cancelled);
        stopped.OrderId.Should().BeNull();
        (await RoundResolutionAsync(await SendAsync(RoundPath + "submit", Submission(intent, prepared)))).Should().Be(stopped);
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared))).Should().Be(stopped);
        (await SendAsync(RoundPath + "prepare", intent)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertNoFinancialEffectsAsync();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerOrderOperation>().SingleAsync()).IsCancelled.Should().BeTrue();
        (await context.Set<TableGuestRoundOperation>().CountAsync()).Should().Be(0);
        Notifications.Verify(value => value.CompleteAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Theory]
    [InlineData("price", HttpStatusCode.Conflict)]
    [InlineData("revision", HttpStatusCode.BadRequest)]
    [InlineData("participant", HttpStatusCode.NotFound)]
    [InlineData("currency", HttpStatusCode.Conflict)]
    [InlineData("native", HttpStatusCode.NotFound)]
    public async Task Changed_review_authority_refuses_effects_but_retains_read_only_recovery_and_original_stopping(string mode, HttpStatusCode expected)
    {
        var intent = await RoundIntentAsync();
        var prepared = await PrepareRoundAsync(intent);
        await using (var context = DatabaseFixture.CreateContext())
        {
            if (mode == "price") (await context.Products.SingleAsync(value => value.Id == ProductId)).BasePrice = 9m;
            if (mode == "revision") (await context.TableServiceSessions.SingleAsync()).RecordAccountChange();
            if (mode == "participant") (await context.TableGuestParticipants.SingleAsync()).RevokedAt = DateTime.UtcNow;
            if (mode == "currency") (await context.RestaurantInfo.SingleAsync()).Currency = "EUR";
            await context.SaveChangesAsync();
        }
        if (mode == "native") WithdrawNative();
        (await SendAsync(RoundPath + "submit", Submission(intent, prepared))).StatusCode.Should().Be(expected);
        await AssertNoFinancialEffectsAsync();
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared, true))).Outcome.Should().Be(CustomerOrderOperationOutcome.Cancelled);
    }
}
