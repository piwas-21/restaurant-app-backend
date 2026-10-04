using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class OrderAmendmentResolutionLookupIntegrationTests(DatabaseFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly Guid AdminId = Guid.Parse(TestAuthHandler.AdminUserId);
    private readonly Guid _pendingOrderId = Guid.NewGuid();
    private readonly Guid _pendingAmendmentId = Guid.NewGuid();
    private readonly Guid _pendingClientOperationId = Guid.NewGuid();
    private readonly Guid _resolvedOrderId = Guid.NewGuid();
    private readonly Guid _resolvedAmendmentId = Guid.NewGuid();
    private readonly Guid _resolvedClientOperationId = Guid.NewGuid();
    private readonly Guid _otherOrderId = Guid.NewGuid();
    private readonly Guid _otherAmendmentId = Guid.NewGuid();
    private readonly Guid _otherClientOperationId = Guid.NewGuid();
    private readonly Guid _refusedOrderId = Guid.NewGuid();
    private readonly Guid _refusedAmendmentId = Guid.NewGuid();
    private readonly Guid _refusedClientOperationId = Guid.NewGuid();

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var now = DateTime.UtcNow;
        context.Orders.AddRange(NewOrder(_pendingOrderId), NewOrder(_resolvedOrderId),
            NewOrder(_otherOrderId), NewOrder(_refusedOrderId));
        context.Set<OrderAmendment>().AddRange(
            NewAmendment(_pendingAmendmentId, _pendingOrderId, AdminId, now),
            NewAmendment(_resolvedAmendmentId, _resolvedOrderId, AdminId, now),
            NewAmendment(_otherAmendmentId, _otherOrderId, Guid.NewGuid(), now),
            NewAmendment(_refusedAmendmentId, _refusedOrderId, AdminId, now));
        var pending = NewOperation(_pendingAmendmentId, _pendingOrderId,
            _pendingClientOperationId, AdminId, OrderAmendmentResolutionOperationState.Processing, now);
        var resolved = NewOperation(_resolvedAmendmentId, _resolvedOrderId,
            _resolvedClientOperationId, AdminId, OrderAmendmentResolutionOperationState.Resolved, now);
        var otherActor = NewOperation(_otherAmendmentId, _otherOrderId,
            _otherClientOperationId, Guid.NewGuid(), OrderAmendmentResolutionOperationState.Processing, now);
        resolved.RefundMinor = 100;
        resolved.UnpaidWaivedMinor = 0;
        resolved.ResolvedAt = now;
        resolved.ResultJson = JsonSerializer.Serialize(new OrderAmendmentResolutionResultDto(
            resolved.Id, resolved.ClientOperationId, resolved.AmendmentId, resolved.SourceOrderId,
            "Resolved", "CHF", 100, 100, 0, now, now, []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        context.OrderAmendmentResolutionOperations.AddRange(pending, resolved, otherActor);
        var refusedRequest = NewExpiredRequest(_refusedClientOperationId, now);
        context.OrderAmendmentResolutionRefusals.Add(new OrderAmendmentResolutionRefusal
        {
            Id = Guid.NewGuid(),
            ActorUserId = AdminId,
            SourceOrderId = _refusedOrderId,
            AmendmentId = _refusedAmendmentId,
            ClientOperationId = _refusedClientOperationId,
            RequestHash = new string('d', 64),
            FailureCode = "quoteExpired",
            OriginalRequestJson = JsonSerializer.Serialize(refusedRequest,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentResolutionLookupIntegrationTests)
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Client_key_get_recovers_pending_and_terminal_results_when_writes_are_disabled()
    {
        using var disabledFactory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantFeatures>();
            services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(new TenantFeatureSettings())));
        }));
        using var admin = disabledFactory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Test-Admin", "true");

        using var pendingResponse = await admin.GetAsync(LookupPath(
            _pendingOrderId, _pendingAmendmentId, _pendingClientOperationId));
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "a lost start response must be recoverable by the quote's client key");
        var pending = await pendingResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions);
        pending!.Data!.Outcome.Should().Be("accepted");
        pending.Data.Result!.State.Should().Be("Processing");
        pending.Data.Result.ClientOperationId.Should().Be(_pendingClientOperationId);
        pending.Data.Result.RefundLegs.Should().BeEmpty();

        using var resolvedResponse = await admin.GetAsync(LookupPath(
            _resolvedOrderId, _resolvedAmendmentId, _resolvedClientOperationId));
        resolvedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolved = await resolvedResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions);
        resolved!.Data!.Outcome.Should().Be("accepted");
        resolved.Data.Result!.State.Should().Be("Resolved");
        resolved.Data.Result.RefundMinor.Should().Be(100);
        resolved.Data.Result.ResolvedAt.Should().NotBeNull();

        using var refusedResponse = await admin.GetAsync(LookupPath(
            _refusedOrderId, _refusedAmendmentId, _refusedClientOperationId));
        refusedResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "a deterministic pre-provider refusal is durable recovery evidence even while writes are off");
        var refused = await refusedResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions);
        refused!.Data!.Outcome.Should().Be("refused");
        refused.Data.Result.Should().BeNull();
        refused.Data.Refusal.Should().NotBeNull();
        refused.Data.Refusal!.FailureCode.Should().Be("quoteExpired");
        refused.Data.Refusal.ActorUserId.Should().Be(AdminId);
        refused.Data.Refusal.OrderId.Should().Be(_refusedOrderId);
        refused.Data.Refusal.AmendmentId.Should().Be(_refusedAmendmentId);
        refused.Data.Refusal.ClientOperationId.Should().Be(_refusedClientOperationId);
        refused.Data.Refusal.OriginalRequest.Quote.ClientOperationId.Should().Be(_refusedClientOperationId);

        using var wrongOrder = await admin.GetAsync(LookupPath(
            Guid.NewGuid(), _pendingAmendmentId, _pendingClientOperationId));
        using var wrongAmendment = await admin.GetAsync(LookupPath(
            _pendingOrderId, Guid.NewGuid(), _pendingClientOperationId));
        wrongOrder.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongAmendment.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var anotherAdmin = await admin.GetAsync(LookupPath(
            _otherOrderId, _otherAmendmentId, _otherClientOperationId));
        anotherAdmin.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the operation belongs to a different administrator even though route identity matches");
        using var wrongRefusalOrder = await admin.GetAsync(LookupPath(
            Guid.NewGuid(), _refusedAmendmentId, _refusedClientOperationId));
        using var wrongRefusalAmendment = await admin.GetAsync(LookupPath(
            _refusedOrderId, Guid.NewGuid(), _refusedClientOperationId));
        wrongRefusalOrder.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongRefusalAmendment.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var blockedQuote = await admin.PostAsJsonAsync(
            $"/api/staff/orders/{_pendingOrderId}/amendments/{_pendingAmendmentId}/financial-resolution/quote",
            new OrderAmendmentResolutionQuoteRequest
            {
                ClientOperationId = Guid.NewGuid(),
                ExpectedOrderVersion = 1,
                Currency = "CHF",
                ManualRefunds = []
            });
        blockedQuote.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the feature flag controls new quotes while actor-scoped recovery remains available");

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderAmendmentResolutionOperations.CountAsync()).Should().Be(3,
            "lookup is read-only and must not create or retry a refund operation");
        (await verify.OrderAmendmentResolutionRefusals.CountAsync()).Should().Be(1,
            "lookup is read-only and does not synthesize refusal receipts");
    }

    [Fact]
    public async Task Start_persists_and_replays_whitelisted_expiry_refusal_before_feature_gate()
    {
        var utcNow = DateTime.UtcNow;
        var hostileUtcNow = new DateTime(utcNow.Ticks - utcNow.Ticks % 10 + 7, DateTimeKind.Utc);
        (hostileUtcNow.Ticks % 10).Should().Be(7);
        using var enabledFactory = EnabledFactory(new RefusalClock(new DateTimeOffset(hostileUtcNow)));
        using var admin = enabledFactory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        var request = NewExpiredRequest(Guid.NewGuid(), hostileUtcNow);
        var path = StartPath(_pendingOrderId, _pendingAmendmentId);

        using var firstResponse = await admin.PostAsJsonAsync(path, request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<ApiResponse<
            OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions);
        first!.Data!.Outcome.Should().Be("refused");
        first.Data.Refusal!.FailureCode.Should().Be("quoteExpired");
        first.Data.Refusal.OrderId.Should().Be(_pendingOrderId);
        first.Data.Refusal.OriginalRequest.Quote.ClientOperationId.Should().Be(request.Quote.ClientOperationId);
        first.Data.Refusal.CreatedAt.Should().Be(hostileUtcNow.AddTicks(-7),
            "the first response must expose the exact timestamp PostgreSQL persists");

        using var disabledFactory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantFeatures>();
            services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(new TenantFeatureSettings())));
        }));
        using var recoveryClient = disabledFactory.CreateClient();
        recoveryClient.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        using var replayResponse = await recoveryClient.PostAsJsonAsync(path, request);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the exact receipt replays before the default-off write gate");
        var replay = await replayResponse.Content.ReadFromJsonAsync<ApiResponse<
            OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions);
        replay!.Data!.Outcome.Should().Be("refused");
        replay.Data.Refusal!.FailureCode.Should().Be("quoteExpired");
        replay.Data.Refusal.CreatedAt.Should().Be(first.Data.Refusal.CreatedAt);

        using var altered = await recoveryClient.PostAsJsonAsync(path,
            request with { Quote = request.Quote with { Currency = "EUR" } });
        altered.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "an operation key cannot be rebound to a different request");

        using var newOperation = await recoveryClient.PostAsJsonAsync(path,
            NewExpiredRequest(Guid.NewGuid(), DateTime.UtcNow));
        newOperation.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a new start remains unavailable while the feature is off");
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderAmendmentResolutionRefusals.CountAsync(value =>
            value.ClientOperationId == request.Quote.ClientOperationId)).Should().Be(1);
        var persisted = await verify.OrderAmendmentResolutionRefusals.AsNoTracking().SingleAsync(value =>
            value.ClientOperationId == request.Quote.ClientOperationId);
        persisted.CreatedAt.Should().Be(first.Data.Refusal.CreatedAt);
    }

    [Fact]
    public async Task Concurrent_cross_route_starts_share_actor_client_key_lock()
    {
        using var enabledFactory = EnabledFactory();
        using var admin = enabledFactory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        var operationId = Guid.NewGuid();
        var first = admin.PostAsJsonAsync(StartPath(_pendingOrderId, _pendingAmendmentId),
            NewExpiredRequest(operationId, DateTime.UtcNow));
        var second = admin.PostAsJsonAsync(StartPath(_resolvedOrderId, _resolvedAmendmentId),
            NewExpiredRequest(operationId, DateTime.UtcNow));
        using var firstResponse = await first;
        using var secondResponse = await second;

        new[] { firstResponse.StatusCode, secondResponse.StatusCode }
            .Should().ContainSingle(value => value == HttpStatusCode.OK);
        new[] { firstResponse.StatusCode, secondResponse.StatusCode }
            .Should().ContainSingle(value => value == HttpStatusCode.Conflict);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderAmendmentResolutionRefusals.CountAsync(value =>
            value.ActorUserId == AdminId && value.ClientOperationId == operationId)).Should().Be(1);
        (await verify.OrderAmendmentResolutionOperations.CountAsync(value =>
            value.ActorUserId == AdminId && value.ClientOperationId == operationId)).Should().Be(0);
    }

    private static string LookupPath(Guid orderId, Guid amendmentId, Guid clientOperationId) =>
        $"/api/staff/orders/{orderId}/amendments/{amendmentId}/financial-resolution/operations/{clientOperationId}";

    private static string StartPath(Guid orderId, Guid amendmentId) =>
        $"/api/staff/orders/{orderId}/amendments/{amendmentId}/financial-resolution";

    private static OrderAmendmentResolutionStartRequest NewExpiredRequest(Guid clientOperationId, DateTime now)
    {
        var paymentIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        return new OrderAmendmentResolutionStartRequest
        {
            Quote = new OrderAmendmentResolutionQuoteRequest
            {
                ClientOperationId = clientOperationId,
                ExpectedOrderVersion = 1,
                Currency = "CHF",
                ManualRefunds = [new() { PaymentId = paymentIds[1], AmountMinor = 100 },
                    new() { PaymentId = paymentIds[0], AmountMinor = 200 }]
            },
            QuoteHash = new string('e', 64),
            ExpiresAt = now.AddMinutes(-1)
        };
    }

    private WebApplicationFactory<Program> EnabledFactory(TimeProvider? clock = null) => Factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantFeatures>();
            var settings = new TenantFeatureSettings { OrderAmendmentsV1 = true };
            services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(settings)));
            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            }
        }));

    private sealed class RefusalClock(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private static Order NewOrder(Guid id) => new()
    {
        Id = id,
        OrderNumber = $"AR-{id:N}"[..15],
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Pending,
        Version = 1,
        OrderDate = DateTime.UtcNow,
        CreatedBy = nameof(OrderAmendmentResolutionLookupIntegrationTests)
    };

    private static OrderAmendment NewAmendment(Guid id, Guid orderId, Guid actorId, DateTime now) => new()
    {
        Id = id,
        SourceOrderId = orderId,
        ActorUserId = actorId,
        ActorRole = "Admin",
        State = OrderAmendmentState.Committed,
        PayloadHash = new string('a', 64),
        CommitPayloadHash = new string('b', 64),
        ExpectedOrderVersion = 1,
        ExpiresAt = now.AddMinutes(1),
        CommittedAt = now,
        RequestJson = "{}",
        ChangesJson = "[]",
        SourceSnapshotJson = "{}",
        FinancialResolutionJson = "{}",
        CreatedBy = nameof(OrderAmendmentResolutionLookupIntegrationTests)
    };

    private static OrderAmendmentResolutionOperation NewOperation(Guid amendmentId, Guid orderId,
        Guid clientOperationId, Guid actorId, OrderAmendmentResolutionOperationState state, DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            AmendmentId = amendmentId,
            SourceOrderId = orderId,
            ClientOperationId = clientOperationId,
            ActorUserId = actorId,
            ActorRole = "Admin",
            ExpectedOrderVersion = 1,
            Currency = "CHF",
            CreditMinor = 100,
            UnpaidWaivedMinor = 100,
            RefundMinor = 0,
            RequestHash = new string('c', 64),
            SnapshotJson = "{}",
            State = state,
            StartedAt = now,
            CreatedBy = nameof(OrderAmendmentResolutionLookupIntegrationTests)
        };
}
