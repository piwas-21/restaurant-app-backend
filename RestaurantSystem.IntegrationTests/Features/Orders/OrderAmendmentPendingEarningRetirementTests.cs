using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task FullSourceRemovalWithUnevaluatedLegacySnapshotAdvertisesExplicitRetirementWithoutMutatingOnGet()
    {
        await SeedUnevaluatedFullVoidAsync();

        AuthenticateAsAdmin();
        using var response = await Client.GetAsync(
            $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution/context");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "staff need an explicit preparation action when exact whole-order removal can retire an unknown earning");
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["data"]!["earningRetirementRequired"]!.GetValue<bool>().Should().BeTrue();
        _refundState.CreateCalls.Should().Be(0);
        _refundState.ListCalls.Should().Be(0);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderAmendmentResolutionOperations.CountAsync()).Should().Be(0,
            "a read-only context must not create a financial-resolution operation");
        (await verify.OrderBillingSnapshots.SingleAsync(value => value.OrderId == _orderId))
            .EarnedPointsCandidate.Should().BeNull("unknown historical earning must stay unknown");
    }

    [Fact]
    public async Task ExplicitRetirementUnlocksExactWholeSourceFinancialQuoteAndReplaysWithoutAnotherRevision()
    {
        await SeedUnevaluatedFullVoidAsync();
        AuthenticateAsAdmin();
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";

        using var contextResponse = await Client.GetAsync($"{path}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var contextBody = JsonNode.Parse(await contextResponse.Content.ReadAsStringAsync())!["data"]!;
        var request = new OrderAmendmentEarningRetirementRequest
        {
            ExpectedOrderVersion = contextBody["expectedOrderVersion"]!.GetValue<int>(),
            ExpectedAccountRevision = contextBody["expectedAccountRevision"]!.GetValue<long>()
        };
        using var prepare = await Client.PostAsJsonAsync(
            $"{path}/prepare-earning-retirement", request, JsonOptions);
        prepare.StatusCode.Should().Be(HttpStatusCode.OK);
        var prepared = (await prepare.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentEarningRetirementDto>>(JsonOptions))!.Data!;
        prepared.State.Should().Be(OrderAmendmentEarningRetirementState.Retired);

        var revisionAfterPrepare = contextBody["expectedAccountRevision"]!.GetValue<long>() + 1;
        var replayTasks = await Task.WhenAll(
            Client.PostAsJsonAsync($"{path}/prepare-earning-retirement", request, JsonOptions),
            Client.PostAsJsonAsync($"{path}/prepare-earning-retirement", request, JsonOptions));
        foreach (var replay in replayTasks)
        {
            using (replay)
                replay.StatusCode.Should().Be(HttpStatusCode.OK,
                    "concurrent retries of the same durable retirement are idempotent");
        }

        await using (var updateAttempt = DatabaseFixture.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                updateAttempt.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE order_billing_earning_retirements
                    SET retired_unit_count = 3 WHERE order_id = {_orderId}
                    """));
            exception.SqlState.Should().Be("23514", "the retirement journal is append-only");
        }
        await using (var deleteAttempt = DatabaseFixture.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                deleteAttempt.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM order_billing_earning_retirements WHERE order_id = {_orderId}
                    """));
            exception.SqlState.Should().Be("23514", "the retirement journal is append-only");
        }

        using var refreshed = await Client.GetAsync($"{path}/context");
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshedBody = JsonNode.Parse(await refreshed.Content.ReadAsStringAsync())!["data"]!;
        refreshedBody["earningRetirementRequired"]!.GetValue<bool>().Should().BeFalse();
        refreshedBody["expectedAccountRevision"]!.GetValue<long>().Should().Be(revisionAfterPrepare);

        var quoteRequest = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = refreshedBody["expectedOrderVersion"]!.GetValue<int>(),
            ExpectedAccountRevision = refreshedBody["expectedAccountRevision"]!.GetValue<long>(),
            Currency = "CHF"
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{path}/quote", quoteRequest, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.CreditMinor.Should().Be(2_000);
        quote.RefundMinor.Should().Be(2_000);
        quote.UnpaidWaivedMinor.Should().Be(0);
        quote.Loyalty.Should().NotBeNull();
        quote.Loyalty!.CandidatePoints.Should().BeNull();
        quote.Loyalty.EarningDisposition.Should().Be(OrderBillingEarningDisposition.Unevaluated);
        quote.Loyalty.EarningRetired.Should().BeTrue();
        quote.Loyalty.AwardPending.Should().BeFalse();
        quote.Loyalty.AppliedAwardPoints.Should().Be(0);
        quote.Loyalty.SuppressedPoints.Should().Be(0);

        await using var verify = DatabaseFixture.CreateContext();
        var retirement = await verify.OrderBillingEarningRetirements.SingleAsync(value => value.OrderId == _orderId);
        retirement.AmendmentId.Should().Be(_amendmentId);
        retirement.RetiredUnitCount.Should().Be(2);
        (await verify.OrderAmendmentResolutionOperations.CountAsync()).Should().Be(0);
        _refundState.CreateCalls.Should().Be(0, "quoting does not perform provider I/O");
    }

    [Fact]
    public async Task AwardAndRetirementSerializeSoUnknownEarningCannotPostAfterRetirement()
    {
        await SeedUnevaluatedFullVoidAsync();
        AuthenticateAsAdmin();
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        using var contextResponse = await Client.GetAsync($"{path}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await contextResponse.Content.ReadAsStringAsync());
        var body = JsonNode.Parse(await contextResponse.Content.ReadAsStringAsync())!["data"]!;
        var request = new OrderAmendmentEarningRetirementRequest
        {
            ExpectedOrderVersion = body["expectedOrderVersion"]!.GetValue<int>(),
            ExpectedAccountRevision = body["expectedAccountRevision"]!.GetValue<long>()
        };

        using var awardScope = Factory.Services.CreateScope();
        var awardService = awardScope.ServiceProvider.GetRequiredService<IFidelityPointsService>();
        var awardTask = awardService.AwardAcceptedOrderAsync(_orderId);
        var retirementTask = Client.PostAsJsonAsync(
            $"{path}/prepare-earning-retirement", request, JsonOptions);
        await Task.WhenAll(awardTask, retirementTask);
        using var retirementResponse = await retirementTask;
        retirementResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var racingAward = await awardTask;
        racingAward.Disposition.Should().BeOneOf(
            FidelityPointsAwardDisposition.Deferred,
            FidelityPointsAwardDisposition.IneligibleAtAcceptance);
        if (racingAward.Disposition == FidelityPointsAwardDisposition.Deferred)
            racingAward.DeferralReason.Should().Be(FidelityPointsAwardDeferralReason.CandidateUnevaluated);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyService = verifyScope.ServiceProvider.GetRequiredService<IFidelityPointsService>();
        var lateAward = await verifyService.AwardAcceptedOrderAsync(_orderId);
        lateAward.Disposition.Should().Be(FidelityPointsAwardDisposition.IneligibleAtAcceptance);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderBillingEarningRetirements.CountAsync(value => value.OrderId == _orderId)).Should().Be(1);
        (await verify.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == _orderId)).Should().BeFalse();
        (await verify.FidelityPointsTransactions.AnyAsync(value =>
            value.OrderId == _orderId && value.TransactionType == TransactionType.Earned)).Should().BeFalse();
    }

    [Fact]
    public async Task PartialRemovalKeepsUnknownEarningHeldAndCannotRetireTheSource()
    {
        await SeedUnevaluatedFullVoidAsync();
        await ChangeCommittedVoidToPartialWithMatchingFinancialPreviewAsync();
        AuthenticateAsAdmin();
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        using var contextResponse = await Client.GetAsync($"{path}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "an unresolved legacy null candidate must remain held, not be presented as a zero-award quote");
        (await contextResponse.Content.ReadAsStringAsync()).Should().Contain("earning evaluation is still pending");
        await using var preconditionContext = DatabaseFixture.CreateContext();
        var preconditions = await preconditionContext.Orders.AsNoTracking()
            .Where(value => value.Id == _orderId)
            .Select(value => new { value.Version, AccountRevision = value.ServiceSession!.AccountRevision })
            .SingleAsync();
        using var prepare = await Client.PostAsJsonAsync($"{path}/prepare-earning-retirement",
            new OrderAmendmentEarningRetirementRequest
            {
                ExpectedOrderVersion = preconditions.Version,
                ExpectedAccountRevision = preconditions.AccountRevision
            }, JsonOptions);
        prepare.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = Factory.Services.CreateScope();
        var awardService = scope.ServiceProvider.GetRequiredService<IFidelityPointsService>();
        var result = await awardService.AwardAcceptedOrderAsync(_orderId);
        result.Disposition.Should().Be(FidelityPointsAwardDisposition.Deferred);
        result.DeferralReason.Should().Be(FidelityPointsAwardDeferralReason.CandidateUnevaluated);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderBillingEarningRetirements.AnyAsync(value => value.OrderId == _orderId)).Should().BeFalse();
        (await verify.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == _orderId)).Should().BeFalse();
    }

    [Theory]
    [InlineData(OrderBillingEarningDisposition.NoCustomerOwnerAtAcceptance)]
    [InlineData(OrderBillingEarningDisposition.LoyaltyModuleDisabledAtAcceptance)]
    public async Task KnownIneligibleAcceptanceAllowsPartialCapturedRefundWithoutRetroactiveAward(
        OrderBillingEarningDisposition disposition)
    {
        var acceptedOwner = disposition == OrderBillingEarningDisposition.LoyaltyModuleDisabledAtAcceptance
            ? Guid.Parse(TestAuthHandler.UserId) : (Guid?)null;
        await SeedThreeUnitPartialCaptureAsync(disposition, acceptedOwner);
        await ChangeCommittedVoidToPartialWithMatchingFinancialPreviewAsync();
        AuthenticateAsAdmin();
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        using var contextResponse = await Client.GetAsync($"{path}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "a frozen known-ineligible disposition is distinct from an unevaluated legacy null");
        var body = JsonNode.Parse(await contextResponse.Content.ReadAsStringAsync())!["data"]!;
        body["earningRetirementRequired"]!.GetValue<bool>().Should().BeFalse();
        body["creditMinor"]!.GetValue<long>().Should().Be(1_500);
        var quoteRequest = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = body["expectedOrderVersion"]!.GetValue<int>(),
            ExpectedAccountRevision = body["expectedAccountRevision"]!.GetValue<long>(),
            Currency = "CHF"
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{path}/quote", quoteRequest, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await quoteResponse.Content.ReadAsStringAsync());
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.CreditMinor.Should().Be(1_500);
        quote.RefundMinor.Should().Be(1_500);
        quote.UnpaidWaivedMinor.Should().Be(0);
        quote.Loyalty.Should().NotBeNull();
        quote.Loyalty!.CandidatePoints.Should().BeNull();
        quote.Loyalty.EarningDisposition.Should().Be(disposition);
        quote.Loyalty.EarningRetired.Should().BeFalse();
        quote.Loyalty.AwardPending.Should().BeFalse();
        quote.Loyalty.AppliedAwardPoints.Should().Be(0);
        quote.Loyalty.SuppressedPoints.Should().Be(0);
        _refundState.CreateCalls.Should().Be(0,
            "a valid quote is read-only until an authorized provider start");

        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.SingleAsync(value => value.Id == _orderId);
            order.UserId = Guid.Parse(TestAuthHandler.UserId);
            await context.SaveChangesAsync();
        }
        using var awardScope = Factory.Services.CreateScope();
        var awardService = awardScope.ServiceProvider.GetRequiredService<IFidelityPointsService>();
        var result = await awardService.AwardAcceptedOrderAsync(_orderId);
        result.Disposition.Should().Be(FidelityPointsAwardDisposition.IneligibleAtAcceptance,
            "later owner attachment or module availability cannot re-evaluate acceptance-time eligibility");
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == _orderId)).Should().BeFalse();
        (await verify.FidelityPointsTransactions.AnyAsync(value =>
            value.OrderId == _orderId && value.TransactionType == TransactionType.Earned)).Should().BeFalse();
    }

    [Fact]
    public async Task RetiredThreeUnitSourceQuotesCapturedUnitRefundAndUnpaidBalanceWaiver()
    {
        await SeedUnevaluatedThreeUnitPartialCaptureAsync();
        AuthenticateAsAdmin();
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";

        using var contextResponse = await Client.GetAsync($"{path}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var contextBody = JsonNode.Parse(await contextResponse.Content.ReadAsStringAsync())!["data"]!;
        contextBody["creditMinor"]!.GetValue<long>().Should().Be(4_500);
        contextBody["earningRetirementRequired"]!.GetValue<bool>().Should().BeTrue();

        using var prepare = await Client.PostAsJsonAsync($"{path}/prepare-earning-retirement",
            new OrderAmendmentEarningRetirementRequest
            {
                ExpectedOrderVersion = contextBody["expectedOrderVersion"]!.GetValue<int>(),
                ExpectedAccountRevision = contextBody["expectedAccountRevision"]!.GetValue<long>()
            }, JsonOptions);
        var prepareBody = await prepare.Content.ReadAsStringAsync();
        prepare.StatusCode.Should().Be(HttpStatusCode.OK, prepareBody);

        using var refreshed = await Client.GetAsync($"{path}/context");
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshedBody = JsonNode.Parse(await refreshed.Content.ReadAsStringAsync())!["data"]!;
        var quoteRequest = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = refreshedBody["expectedOrderVersion"]!.GetValue<int>(),
            ExpectedAccountRevision = refreshedBody["expectedAccountRevision"]!.GetValue<long>(),
            Currency = "CHF"
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{path}/quote", quoteRequest, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;

        quote.CreditMinor.Should().Be(4_500);
        quote.RefundMinor.Should().Be(1_500,
            "refund scope must follow the original captured allocation, not the unpaid balance");
        quote.UnpaidWaivedMinor.Should().Be(3_000);
        quote.RefundLegs.Should().ContainSingle();
        quote.RefundLegs[0].AmountMinor.Should().Be(1_500);
        quote.RefundLegs[0].Scopes.Should().ContainSingle(scope =>
            scope.OrderItemId == _itemId && scope.StartOrdinal == 1
            && scope.UnitCount == 1 && scope.AmountMinor == 1_500);
        quote.Loyalty.Should().NotBeNull();
        quote.Loyalty!.CandidatePoints.Should().BeNull();
        quote.Loyalty.EarningDisposition.Should().Be(OrderBillingEarningDisposition.Unevaluated);
        quote.Loyalty.EarningRetired.Should().BeTrue();
        _refundState.CreateCalls.Should().Be(0,
            "retirement and quote preparation must not contact the payment provider");
    }

    [Fact]
    public async Task RetirementRejectsStalePreconditionsAndPreviewCreditThatDoesNotMatchAcceptedMoney()
    {
        await SeedUnevaluatedFullVoidAsync();
        AuthenticateAsAdmin();
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution/prepare-earning-retirement";

        using var stale = await Client.PostAsJsonAsync(path,
            new OrderAmendmentEarningRetirementRequest
            {
                ExpectedOrderVersion = 2,
                ExpectedAccountRevision = 1
            }, JsonOptions);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var context = DatabaseFixture.CreateContext())
        {
            var amendment = await context.OrderAmendments.SingleAsync(value => value.Id == _amendmentId);
            amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(
                new OrderAmendmentFinancialPreviewDto("CHF", 0, 2_100, -2_100, 2_100,
                    OrderAmendmentFinancialResolutionStatus.Pending,
                    OrderAmendmentCreditState.PendingAllocationReview,
                    OrderAmendmentLoyaltyState.None,
                    OrderAmendmentRefundState.GatewayRefundRequired));
            await context.SaveChangesAsync();
        }
        using var wrongCredit = await Client.PostAsJsonAsync(path,
            new OrderAmendmentEarningRetirementRequest
            {
                ExpectedOrderVersion = 1,
                ExpectedAccountRevision = 1
            }, JsonOptions);
        wrongCredit.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderBillingEarningRetirements.CountAsync(value => value.OrderId == _orderId)).Should().Be(0);
    }

    private async Task SeedUnevaluatedFullVoidAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var source = await context.Orders.Include(value => value.Items)
            .SingleAsync(value => value.Id == _orderId);
        var snapshot = OrderBillingSnapshotFactory.Build(source, "CHF",
            earningEvaluation: null, redemption: null, maximumUnitRows: 1_000);
        context.OrderBillingSnapshots.Add(snapshot.Header);
        context.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
        context.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);

        var amendment = await context.OrderAmendments.SingleAsync(value => value.Id == _amendmentId);
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
                OrderAmendmentLoyaltyState.None,
                OrderAmendmentRefundState.GatewayRefundRequired));
        await context.SaveChangesAsync();
    }

    private Task SeedUnevaluatedThreeUnitPartialCaptureAsync() =>
        SeedThreeUnitPartialCaptureAsync(null, null);

    private async Task SeedThreeUnitPartialCaptureAsync(
        OrderBillingEarningDisposition? disposition, Guid? ownerAtAcceptance)
    {
        await using var context = DatabaseFixture.CreateContext();
        var source = await context.Orders.Include(value => value.Items).Include(value => value.Payments)
            .SingleAsync(value => value.Id == _orderId);
        source.UserId = ownerAtAcceptance;
        var item = source.Items.Single();
        item.Quantity = 3;
        item.UnitPrice = 15m;
        item.ItemTotal = 45m;
        source.Status = OrderStatus.Confirmed;
        source.PaymentStatus = PaymentStatus.PartiallyPaid;
        source.SubTotal = 45m;
        source.Total = 45m;
        source.TotalPaid = 15m;
        source.RemainingAmount = 30m;

        var payment = source.Payments.Single();
        payment.Amount = 15m;
        payment.Status = PaymentStatus.Completed;

        var attempt = await context.AccountPaymentAttempts.Include(value => value.Allocations)
            .SingleAsync(value => value.Id == _attemptId);
        attempt.AmountMinor = 1_500;
        var allocation = attempt.Allocations.Single();
        allocation.StartOrdinal = 1;
        allocation.UnitCount = 1;
        allocation.MinorPerUnit = 1_500;
        allocation.AmountMinor = 1_500;
        var journal = await context.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
        journal.AmountMinor = 1_500;
        journal.ProviderCapturedMinor = 1_500;

        var amendment = await context.OrderAmendments.SingleAsync(value => value.Id == _amendmentId);
        var change = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(
            amendment.ChangesJson).Single();
        amendment.ChangesJson = OrderAmendmentJson.Serialize(new[] { change with
        {
            Quantity = 3,
            Previous = change.Previous with { Quantity = 3 }
        } });
        amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(
            new OrderAmendmentFinancialPreviewDto("CHF", 0, 4_500, -4_500, 4_500,
                OrderAmendmentFinancialResolutionStatus.Pending,
                OrderAmendmentCreditState.PendingAllocationReview,
                OrderAmendmentLoyaltyState.None,
                OrderAmendmentRefundState.GatewayRefundRequired));

        await context.SaveChangesAsync();

        var evaluation = disposition.HasValue
            ? new OrderBillingEarningEvaluation(null, null, null, null, disposition.Value)
            : null;
        var snapshot = OrderBillingSnapshotFactory.Build(source, "CHF",
            earningEvaluation: evaluation, redemption: null, maximumUnitRows: 1_000);
        context.OrderBillingSnapshots.Add(snapshot.Header);
        context.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
        context.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);
        await context.SaveChangesAsync();
    }

    private async Task ChangeCommittedVoidToPartialWithMatchingFinancialPreviewAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var source = await context.Orders.Include(value => value.Items)
            .Include(value => value.Payments).Include(value => value.ServiceSession)
            .SingleAsync(value => value.Id == _orderId);
        var amendment = await context.OrderAmendments.SingleAsync(value => value.Id == _amendmentId);
        var changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
        changes.Should().ContainSingle();
        changes[0] = changes[0] with { Quantity = 1 };
        amendment.ChangesJson = OrderAmendmentJson.Serialize(changes);
        using var serviceScope = Factory.Services.CreateScope();
        var financial = serviceScope.ServiceProvider.GetRequiredService<IOrderAmendmentFinancialResolution>();
        var preview = await financial.PreviewAsync(source, changes, null, CancellationToken.None);
        amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(preview);
        await context.SaveChangesAsync();
    }
}
