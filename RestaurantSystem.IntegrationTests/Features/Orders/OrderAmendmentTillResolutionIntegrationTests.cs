using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed partial class OrderAmendmentTillResolutionIntegrationTests(DatabaseFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly Guid AdminId = Guid.Parse(TestAuthHandler.AdminUserId);
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _amendmentId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _paymentId = Guid.NewGuid();
    private readonly Guid _clientOperationId = Guid.NewGuid();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantFeatures>();
        services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(
            new TenantFeatureSettings { OrderAmendmentsV1 = true })));
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(CreateSubMicrosecondUtcNow()));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var now = DateTime.UtcNow;
        context.Orders.Add(new Order
        {
            Id = _orderId,
            OrderNumber = $"TL-{_orderId:N}"[..15],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            Version = 1,
            SubTotal = 20m,
            Total = 20m,
            TotalPaid = 20m,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentTillResolutionIntegrationTests),
            Items = [new OrderItem
            {
                Id = _itemId,
                ProductName = "Two-unit meal",
                Quantity = 2,
                UnitPrice = 10m,
                ItemTotal = 20m,
                CreatedBy = nameof(OrderAmendmentTillResolutionIntegrationTests)
            }],
            Payments = [new OrderPayment
            {
                Id = _paymentId,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 20m,
                Currency = "CHF",
                Status = PaymentStatus.Completed,
                PaymentDate = now,
                CreatedAt = now,
                CreatedBy = nameof(OrderAmendmentTillResolutionIntegrationTests)
            }]
        });
        context.OrderAmendments.Add(NewAmendment(now));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Till_refund_is_durable_before_attestation_and_recovery_returns_exact_proof()
    {
        AuthenticateAsAdmin();
        var basePath = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        using var contextResponse = await Client.GetAsync($"{basePath}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var context = (await contextResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionContextDto>>(JsonOptions))!.Data!;
        context.Currency.Should().Be("CHF");
        context.ManualRefundCandidates.Should().ContainSingle(candidate =>
            candidate.PaymentId == _paymentId && candidate.PaymentMethod == "Cash"
            && candidate.AvailableMinor == 2000);

        var quoteRequest = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = 1,
            Currency = "CHF",
            ManualRefunds = [new ManualRefundSelectionRequest { PaymentId = _paymentId, AmountMinor = 1000 }]
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{basePath}/quote", quoteRequest, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.RefundLegs.Should().ContainSingle(leg => leg.RequiresTillConfirmation
            && leg.Custody == "ManualTill" && leg.AmountMinor == 1000);

        var startRequest = new OrderAmendmentResolutionStartRequest
        {
            Quote = quoteRequest,
            QuoteHash = quote.QuoteHash,
            ExpiresAt = quote.ExpiresAt
        };
        using var startResponse = await Client.PostAsJsonAsync(basePath, startRequest, JsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = (await startResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        start.Outcome.Should().Be("accepted");
        var operation = start.Result!;
        operation.State.Should().Be("Processing");
        operation.RefundLegs.Should().ContainSingle(leg => leg.State == "Pending"
            && leg.TillConfirmation == null);

        await using (var kitchenUpdate = DatabaseFixture.CreateContext())
        {
            await kitchenUpdate.Orders.Where(value => value.Id == _orderId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Status, OrderStatus.Preparing)
                    .SetProperty(value => value.IsKitchenReleased, true)
                    .SetProperty(value => value.Version, value => value.Version + 1));
        }

        using var disabledFactory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantFeatures>();
            services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(new TenantFeatureSettings())));
        }));
        using var recoveryClient = disabledFactory.CreateClient();
        recoveryClient.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        using var recoveredPending = await recoveryClient.GetAsync(
            $"{basePath}/operations/{_clientOperationId}");
        recoveredPending.StatusCode.Should().Be(HttpStatusCode.OK);
        var pendingResult = (await recoveredPending.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!.Result!;
        pendingResult.RefundLegs.Single().TillConfirmation.Should().BeNull();
        using var replayStart = await recoveryClient.PostAsJsonAsync(basePath, startRequest, JsonOptions);
        replayStart.StatusCode.Should().Be(HttpStatusCode.OK,
            "an existing operation remains replayable while new starts are disabled");

        await using (var beforeTill = DatabaseFixture.CreateContext())
        {
            (await beforeTill.OrderAmendmentRefundEvidence.CountAsync(value =>
                value.Kind == OrderAmendmentRefundEvidenceKind.ManualTillConfirmation)).Should().Be(0);
            (await beforeTill.OrderPayments.SingleAsync(value => value.Id == _paymentId))
                .RefundedAmount.Should().BeNull();
        }

        var invalid = await recoveryClient.PostAsJsonAsync(ConfirmPath(operation.OperationId),
            new ManualTillConfirmationRequest { PaymentId = _paymentId, TillReference = "drawer return" },
            JsonOptions);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        const string tillReference = "cash-2026/0042#1";
        var confirmationRequest = new ManualTillConfirmationRequest
        {
            PaymentId = _paymentId,
            TillReference = tillReference
        };
        using var confirmedResponse = await recoveryClient.PostAsJsonAsync(
            ConfirmPath(operation.OperationId), confirmationRequest, JsonOptions);
        confirmedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = (await confirmedResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
        confirmed.State.Should().Be("Resolved");
        confirmed.RefundLegs.Should().ContainSingle(leg => leg.State == "Succeeded"
            && leg.TillConfirmation!.TillReference == tillReference);

        using var operationReadback = await recoveryClient.GetAsync(
            $"/api/staff/amendment-financial-resolution-operations/{operation.OperationId}");
        operationReadback.StatusCode.Should().Be(HttpStatusCode.OK);
        var readback = (await operationReadback.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
        readback.RefundLegs.Single().TillConfirmation.Should().Be(confirmed.RefundLegs.Single().TillConfirmation);

        using var identicalRetry = await recoveryClient.PostAsJsonAsync(
            ConfirmPath(operation.OperationId), confirmationRequest, JsonOptions);
        identicalRetry.StatusCode.Should().Be(HttpStatusCode.OK);
        using var changedRetry = await recoveryClient.PostAsJsonAsync(ConfirmPath(operation.OperationId),
            confirmationRequest with { TillReference = "different-refund" }, JsonOptions);
        changedRetry.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderAmendmentRefundEvidence.CountAsync(value =>
            value.Kind == OrderAmendmentRefundEvidenceKind.ManualTillConfirmation)).Should().Be(1);
        (await verify.OrderBillingCredits.CountAsync(value => value.AmendmentId == _amendmentId)).Should().Be(1);
        var persistedOrder = await verify.Orders.SingleAsync(value => value.Id == _orderId);
        persistedOrder.BillingCreditAmount.Should().Be(10m);
        persistedOrder.TotalPaid.Should().Be(10m);
        persistedOrder.RemainingAmount.Should().Be(0m);
        var payment = await verify.OrderPayments.SingleAsync(value => value.Id == _paymentId);
        payment.RefundedAmount.Should().Be(10m);
        payment.Status.Should().Be(PaymentStatus.PartiallyRefunded);

        await using (var timestampProbe = DatabaseFixture.CreateContext())
        {
            var persistedOperation = await timestampProbe.OrderAmendmentResolutionOperations
                .Include(value => value.Legs)
                .SingleAsync(value => value.Id == operation.OperationId);
            var savedResult = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionResultDto>(
                persistedOperation.ResultJson!);
            (savedResult.ResolvedAt!.Value.Ticks % 10).Should().Be(7);
            (persistedOperation.ResolvedAt!.Value.Ticks % 10).Should().Be(0);
            persistedOperation.ResolvedAt.Should().Be(savedResult.ResolvedAt.Value.AddTicks(-7),
                "PostgreSQL truncates result timestamps to microsecond precision on reload");
            savedResult.ResolvedAt.Should().NotBe(persistedOperation.ResolvedAt,
                "the fixed clock has sub-microsecond ticks that PostgreSQL cannot store in timestamp columns");
            (savedResult.RefundLegs.Single().ResolvedAt!.Value.Ticks % 10).Should().Be(7);
            (persistedOperation.Legs.Single().ResolvedAt!.Value.Ticks % 10).Should().Be(0);
            savedResult.RefundLegs.Single().ResolvedAt.Should().NotBe(
                persistedOperation.Legs.Single().ResolvedAt,
                "the durable DTO currently retains finer timestamp precision than its source columns");
        }

        using var eligibility = await Client.GetAsync($"/api/staff/orders/{_orderId}/amendments/eligibility");
        eligibility.StatusCode.Should().Be(HttpStatusCode.OK);
        var eligible = (await eligibility.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentEligibilityDto>>(JsonOptions))!.Data!;
        eligible.CanCreateAmendment.Should().BeTrue(
            "the canonical resolved partial refund and prior credit remain eligible for later amendments; "
            + $"reason was {eligible.ReasonCode}");
        eligible.AmendmentMode.Should().Be("Native");
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
            OrderAmendmentRefundState.PendingTillRefund);
        return new OrderAmendment
        {
            Id = _amendmentId,
            SourceOrderId = _orderId,
            ActorUserId = AdminId,
            ActorRole = UserRole.Admin.ToString(),
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            CommitPayloadHash = new string('b', 64),
            ExpectedOrderVersion = 1,
            ExpiresAt = now.AddMinutes(5),
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = OrderAmendmentJson.Serialize(new[] { change }),
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = OrderAmendmentJson.Serialize(financial),
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentTillResolutionIntegrationTests)
        };
    }

    private static string ConfirmPath(Guid operationId) =>
        $"/api/staff/amendment-financial-resolution-operations/{operationId}/confirm-till";

    private static DateTimeOffset CreateSubMicrosecondUtcNow()
    {
        var nextMicrosecond = (DateTimeOffset.UtcNow.UtcTicks / 10 + 1) * 10;
        return new DateTimeOffset(nextMicrosecond + 7, TimeSpan.Zero);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
