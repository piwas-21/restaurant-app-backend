using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed partial class OrderAmendmentStripeResolutionIntegrationTests(DatabaseFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly Guid AdminId = Guid.Parse(TestAuthHandler.AdminUserId);
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _paymentId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();
    private readonly Guid _amendmentId = Guid.NewGuid();
    private readonly Guid _clientOperationId = Guid.NewGuid();
    private readonly FakeAmendmentRefundState _refundState = new();
    private readonly MutableAmendmentFeatures _features = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantFeatures>();
        services.AddSingleton<ITenantFeatures>(_features);
        services.RemoveAll<IOrderAmendmentRefundProvider>();
        services.AddSingleton(_refundState);
        services.AddScoped<FakeAmendmentRefundProvider>();
        services.AddScoped<IOrderAmendmentRefundProvider>(provider =>
            provider.GetRequiredService<FakeAmendmentRefundProvider>());
        services.RemoveAll<IAccountCheckoutEvidenceReader>();
        services.AddScoped<IAccountCheckoutEvidenceReader>(provider =>
            new FakeAmendmentCheckoutEvidenceReader(provider.GetRequiredService<FakeAmendmentRefundState>()));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var now = DateTime.UtcNow;
        var session = new TableServiceSession
        {
            Id = _sessionId,
            TableNumber = 987,
            Currency = "CHF",
            AccountRevision = 1,
            BillingAllocationVersion = 1,
            OpenedAt = now,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        };
        var item = new OrderItem
        {
            Id = _itemId,
            ProductName = "Two-unit meal",
            Quantity = 2,
            UnitPrice = 10m,
            ItemTotal = 20m,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        };
        var payment = new OrderPayment
        {
            Id = _paymentId,
            PaymentMethod = PaymentMethod.OnlinePayment,
            Amount = 20m,
            Currency = "CHF",
            Status = PaymentStatus.Completed,
            TransactionId = FakeAmendmentRefundState.ChargeId,
            PaymentGateway = "Stripe",
            PaymentDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        };
        var order = new Order
        {
            Id = _orderId,
            OrderNumber = $"SR-{_orderId:N}"[..15],
            Type = OrderType.DineIn,
            ServiceSessionId = _sessionId,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            Version = 1,
            SubTotal = 20m,
            Total = 20m,
            TotalPaid = 20m,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests),
            Items = [item],
            Payments = [payment]
        };
        var attempt = new AccountPaymentAttempt
        {
            Id = _attemptId,
            ServiceSessionId = _sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.GuestParticipant,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Captured,
            PaymentMethod = PaymentMethod.OnlinePayment,
            AmountMinor = 2000,
            Currency = "CHF",
            PayloadHash = new string('c', 64),
            SnapshotJson = "{}",
            ExpectedAccountRevision = 1,
            QuoteExpiresAt = now.AddHours(1),
            StartedAt = now,
            CompletedAt = now,
            ProviderSessionId = FakeAmendmentRefundState.SessionId,
            ProviderChargeId = FakeAmendmentRefundState.ChargeId,
            ProviderAccountId = FakeAmendmentRefundState.AccountId,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests),
            Allocations = [new AccountPaymentAllocation
            {
                Id = Guid.NewGuid(),
                OrderId = _orderId,
                OrderItemId = _itemId,
                OrderPaymentId = _paymentId,
                StartOrdinal = 1,
                UnitCount = 2,
                MinorPerUnit = 1000,
                AmountMinor = 2000,
                CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
            }]
        };
        var journal = new AccountCheckoutJournal
        {
            Id = Guid.NewGuid(),
            AttemptId = _attemptId,
            StartedAttemptVersion = 1,
            AmountMinor = 2000,
            Currency = "CHF",
            ProviderAccountId = FakeAmendmentRefundState.AccountId,
            ProviderLiveMode = false,
            CreateIdempotencyKey = $"checkout:{_attemptId:N}",
            CreatePayloadHash = new string('a', 64),
            ReturnBaseUrl = "https://tenant.test/table-account",
            StartedAt = now,
            ExpiresAt = now.AddHours(1),
            MaximumCreateRetryAt = now.AddHours(1),
            ProviderSessionId = FakeAmendmentRefundState.SessionId,
            ProviderIntentId = FakeAmendmentRefundState.IntentId,
            ProviderChargeId = FakeAmendmentRefundState.ChargeId,
            ProviderCapturedMinor = 2000,
            ProviderRefundedMinor = 0,
            LeaseId = Guid.NewGuid(),
            LeaseExpiresAt = now.AddHours(1),
            LastVerifiedAt = now,
            NextReconcileAt = now.AddDays(1),
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        };
        context.Add(session);
        context.Add(order);
        context.Add(attempt);
        context.Add(journal);
        context.Add(NewAmendment(now));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Stripe_refund_timeout_is_held_then_exact_feature_off_replay_posts_one_bound_reversal()
    {
        AuthenticateAsAdmin();
        var basePath = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        var request = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            Currency = "CHF"
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{basePath}/quote", request, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.RefundLegs.Should().ContainSingle(value => value.Custody == "StripeDirect"
            && value.AmountMinor == 1000 && !value.RequiresTillConfirmation);

        var startRequest = new OrderAmendmentResolutionStartRequest
        {
            Quote = request,
            QuoteHash = quote.QuoteHash,
            ExpiresAt = quote.ExpiresAt
        };
        using var startResponse = await Client.PostAsJsonAsync(basePath, startRequest, JsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = (await startResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        start.Outcome.Should().Be("accepted");
        var pending = start.Result!;
        pending.State.Should().Be("ReconciliationRequired");
        pending.RefundLegs.Should().ContainSingle(value => value.State == "ReconciliationRequired");
        _refundState.CreateCalls.Should().Be(1);
        _refundState.LastRequest.Should().NotBeNull();
        _refundState.LastRequest!.AmountMinor.Should().Be(1000);
        _refundState.LastRequest.ChargeId.Should().Be(FakeAmendmentRefundState.ChargeId);
        _refundState.LastRequest.Currency.Should().Be("CHF");
        _refundState.ProviderIoObservedDatabaseTransaction.Should().BeFalse();
        _refundState.ProviderRequestWasDurableBeforeCreate.Should().BeTrue();
        _refundState.LastRequest.IdempotencyKey.Should().MatchRegex("^amendment-refund:[0-9a-f]{32}$");

        await using (var held = DatabaseFixture.CreateContext())
        {
            (await held.OrderBillingCredits.CountAsync(value => value.AmendmentId == _amendmentId)).Should().Be(0);
            (await held.AccountPaymentAllocationReversals.CountAsync()).Should().Be(0);
            var order = await held.Orders.SingleAsync(value => value.Id == _orderId);
            order.BillingCreditAmount.Should().Be(0m);
            order.TotalPaid.Should().Be(20m);
            var payment = await held.OrderPayments.SingleAsync(value => value.Id == _paymentId);
            payment.RefundedAmount.Should().BeNull();
            var accountAttempt = await held.AccountPaymentAttempts.SingleAsync(value => value.Id == _attemptId);
            accountAttempt.State.Should().Be(AccountPaymentState.Captured);
        }

        _features.OrderAmendmentsV1 = false;
        using var pendingRead = await Client.GetAsync($"{basePath}/operations/{_clientOperationId}");
        pendingRead.StatusCode.Should().Be(HttpStatusCode.OK,
            "a feature-off recovery read must retain the accepted durable request");
        var pendingOutcome = (await pendingRead.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        pendingOutcome.Outcome.Should().Be("accepted");
        pendingOutcome.Result!.State.Should().Be("ReconciliationRequired");

        var listCallsBeforeRecovery = _refundState.ListCalls;
        using var freshClient = Factory.CreateClient();
        freshClient.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        using var amendmentRecovery = await freshClient.GetAsync($"{basePath}/recovery");
        amendmentRecovery.StatusCode.Should().Be(HttpStatusCode.OK);
        var recovered = (await amendmentRecovery.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionRecoveryDto>>(JsonOptions))!.Data!;
        recovered.OriginalRequest.Quote.ClientOperationId.Should().Be(_clientOperationId);
        recovered.OriginalRequest.QuoteHash.Should().Be(startRequest.QuoteHash);
        recovered.OriginalRequest.ExpiresAt.Should().Be(startRequest.ExpiresAt);
        recovered.OriginalRequest.Quote.ExpectedOrderVersion.Should().Be(request.ExpectedOrderVersion);
        recovered.ReviewedQuote.Should().BeEquivalentTo(quote);
        recovered.Result.OperationId.Should().Be(pending.OperationId);
        recovered.Result.State.Should().Be("ReconciliationRequired");

        using var orderRecovery = await freshClient.GetAsync(
            $"/api/staff/orders/{_orderId}/amendment-financial-resolution-recovery");
        orderRecovery.StatusCode.Should().Be(HttpStatusCode.OK);
        var recoverable = (await orderRecovery.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionRecoveryDto[]>>(JsonOptions))!.Data!;
        recoverable.Should().ContainSingle(value => value.Result.OperationId == pending.OperationId);
        _refundState.ListCalls.Should().Be(listCallsBeforeRecovery,
            "owner discovery reads persisted state and must never call the provider");
        _refundState.CreateCalls.Should().Be(1);

        using var otherActorFactory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddScoped<ICurrentUserService>(_ => new FixedAdminCurrentUserService(Guid.NewGuid()));
        }));
        using var otherActorClient = otherActorFactory.CreateClient();
        otherActorClient.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        using var wrongOwnerRecovery = await otherActorClient.GetAsync($"{basePath}/recovery");
        wrongOwnerRecovery.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var otherOwnerList = await otherActorClient.GetAsync(
            $"/api/staff/orders/{_orderId}/amendment-financial-resolution-recovery");
        otherOwnerList.StatusCode.Should().Be(HttpStatusCode.OK);
        (await otherOwnerList.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionRecoveryDto[]>>(JsonOptions))!.Data.Should().BeEmpty();

        using var exactReplay = await Client.PostAsJsonAsync(basePath, startRequest, JsonOptions);
        exactReplay.StatusCode.Should().Be(HttpStatusCode.OK,
            "an exact accepted start replay must reconcile the same provider request after flag-off");
        var replay = (await exactReplay.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        replay.Outcome.Should().Be("accepted");
        replay.Result!.OperationId.Should().Be(pending.OperationId);
        replay.Result.State.Should().Be("Resolved");
        replay.Result.RefundLegs.Should().ContainSingle(value => value.State == "Succeeded");
        _refundState.CreateCalls.Should().Be(1, "the persisted provider refund must be adopted, not recreated");

        using var resultRead = await Client.GetAsync(
            $"/api/staff/amendment-financial-resolution-operations/{pending.OperationId}");
        resultRead.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await resultRead.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
        result.RefundLegs.Should().ContainSingle(value => value.State == "Succeeded");
        _refundState.CreatedEvidence.Should().ContainSingle();

        await using var settled = DatabaseFixture.CreateContext();
        var operation = await settled.OrderAmendmentResolutionOperations.SingleAsync(value => value.Id == pending.OperationId);
        operation.State.Should().Be(OrderAmendmentResolutionOperationState.Resolved);
        var leg = await settled.OrderAmendmentRefundLegs.Include(value => value.Attempts)
            .SingleAsync(value => value.OperationId == operation.Id);
        var providerEvidence = await settled.OrderAmendmentRefundEvidence.SingleAsync(value =>
            value.RefundLegId == leg.Id && value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation
            && value.ProviderRefundId != null);
        providerEvidence.ProviderRefundId.Should().Be(_refundState.CreatedEvidence.Single().RefundId);
        providerEvidence.ProviderRefundStatus.Should().Be("succeeded");
        providerEvidence.RefundAttemptId.Should().Be(leg.Attempts.Single().Id);
        var reversal = await settled.AccountPaymentAllocationReversals.SingleAsync();
        reversal.AllocationId.Should().Be(ReadAllocationId(settled, _attemptId));
        reversal.StartOrdinal.Should().Be(1);
        reversal.UnitCount.Should().Be(1);
        reversal.AmountMinor.Should().Be(1000);
        (await settled.OrderBillingCredits.SingleAsync(value => value.AmendmentId == _amendmentId))
            .AmountMinor.Should().Be(1000);
        var finalOrder = await settled.Orders.SingleAsync(value => value.Id == _orderId);
        finalOrder.BillingCreditAmount.Should().Be(10m);
        finalOrder.TotalPaid.Should().Be(10m);
        finalOrder.RemainingAmount.Should().Be(0m);
        var finalPayment = await settled.OrderPayments.SingleAsync(value => value.Id == _paymentId);
        finalPayment.RefundedAmount.Should().Be(10m);
        finalPayment.Status.Should().Be(PaymentStatus.PartiallyRefunded);
    }

    [Fact]
    public async Task Recovery_without_saved_original_request_and_reviewed_quote_fails_closed()
    {
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.OrderAmendmentResolutionOperations.Add(new OrderAmendmentResolutionOperation
            {
                Id = Guid.NewGuid(),
                AmendmentId = _amendmentId,
                SourceOrderId = _orderId,
                ServiceSessionId = _sessionId,
                ClientOperationId = _clientOperationId,
                ActorUserId = AdminId,
                ActorRole = UserRole.Admin.ToString(),
                ExpectedOrderVersion = 1,
                ExpectedAccountRevision = 1,
                Currency = "CHF",
                CreditMinor = 1000,
                RefundMinor = 1000,
                UnpaidWaivedMinor = 0,
                RequestHash = new string('d', 64),
                SnapshotJson = "{\"quoteHash\":\"legacy\",\"expiresAt\":\"2026-10-03T12:00:00Z\","
                    + "\"currency\":\"CHF\",\"creditMinor\":1000,\"refundMinor\":1000,"
                    + "\"unpaidWaivedMinor\":0,\"requestHash\":\"legacy\","
                    + "\"sourceFinancialFingerprint\":\"legacy\",\"planFingerprint\":\"legacy\"}",
                State = OrderAmendmentResolutionOperationState.ReconciliationRequired,
                StartedAt = DateTime.UtcNow,
                CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
            });
            await context.SaveChangesAsync();
        }

        AuthenticateAsAdmin();
        var path = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution/recovery";
        using var recovery = await Client.GetAsync(path);
        recovery.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "missing accepted-request evidence must not be treated as permission to mint a fresh quote");
        using var discover = await Client.GetAsync(
            $"/api/staff/orders/{_orderId}/amendment-financial-resolution-recovery");
        discover.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _refundState.CreateCalls.Should().Be(0);
        _refundState.ListCalls.Should().Be(0);

        await using (var context = DatabaseFixture.CreateContext())
        {
            var operation = await context.OrderAmendmentResolutionOperations
                .SingleAsync(value => value.ClientOperationId == _clientOperationId);
            operation.SnapshotJson = "null";
            await context.SaveChangesAsync();
        }

        using var malformedRecovery = await Client.GetAsync(path);
        malformedRecovery.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "corrupt persisted intent remains held instead of becoming a server error or a fresh quote");
        using var malformedDiscover = await Client.GetAsync(
            $"/api/staff/orders/{_orderId}/amendment-financial-resolution-recovery");
        malformedDiscover.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _refundState.CreateCalls.Should().Be(0);
        _refundState.ListCalls.Should().Be(0);
    }

    private OrderAmendment NewAmendment(DateTime now)
    {
        var previous = new RestaurantSystem.Api.Features.Orders.Dtos.OrderItemDto
        {
            Id = _itemId,
            ProductName = "Two-unit meal",
            Quantity = 2,
            UnitPrice = 10m,
            ItemTotal = 20m
        };
        var change = new OrderAmendmentChangeSnapshot(
            _itemId, OrderAmendmentChangeKind.Void, 1, 1, false, previous, null);
        var financial = new OrderAmendmentFinancialPreviewDto("CHF", 0, 1000, -1000, 1000,
            OrderAmendmentFinancialResolutionStatus.Pending,
            OrderAmendmentCreditState.PendingAllocationReview,
            OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.GatewayRefundRequired);
        return new OrderAmendment
        {
            Id = _amendmentId,
            SourceOrderId = _orderId,
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
            ChangesJson = OrderAmendmentJson.Serialize(new[] { change }),
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = OrderAmendmentJson.Serialize(financial),
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        };
    }

    private static Guid ReadAllocationId(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context, Guid attemptId) =>
        context.AccountPaymentAllocations.AsNoTracking().Single(value => value.AttemptId == attemptId).Id;
}
