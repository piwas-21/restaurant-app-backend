using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed partial class OrderBillingKnownIneligibleCreditIntegrationTests(DatabaseFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const int TableNumber = 74;
    private const long SeededTestPizzaPriceMinor = 1_299;
    private readonly Guid _tableId = Guid.NewGuid();
    private readonly MutableTestTenantModules _modules = new();
    private Guid _productId;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<TenantFeatureSettings>(settings => settings.OrderAmendmentsV1 = true);
        services.RemoveAll<ITenantModules>();
        services.AddSingleton<ITenantModules>(_modules);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products.Where(value => value.Name == "Test Pizza")
            .Select(value => value.Id).SingleAsync();
        context.Tables.Add(new Table
        {
            Id = _tableId,
            TableNumber = TableNumber.ToString(),
            MaxGuests = 4,
            IsActive = true,
            CreatedBy = nameof(OrderBillingKnownIneligibleCreditIntegrationTests)
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Unpaid_credit_commits_for_staff_round_without_owner_even_when_loyalty_is_enabled()
    {
        _modules.LoyaltyEnabled = true;
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        await CreateAndVoidRoundAsync(session.ServiceSessionId, customerUserId: null,
            OrderBillingEarningDisposition.NoCustomerOwnerAtAcceptance);
    }

    [Fact]
    public async Task Unpaid_credit_commits_for_customer_round_when_loyalty_was_disabled_at_acceptance()
    {
        _modules.LoyaltyEnabled = false;
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        await CreateAndVoidRoundAsync(session.ServiceSessionId, Guid.Parse(TestAuthHandler.UserId),
            OrderBillingEarningDisposition.LoyaltyModuleDisabledAtAcceptance);
    }

    [Fact]
    public async Task Evaluated_zero_candidate_still_allows_unpaid_credit_commit()
    {
        _modules.LoyaltyEnabled = true;
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        await CreateAndVoidRoundAsync(session.ServiceSessionId, Guid.Parse(TestAuthHandler.UserId),
            OrderBillingEarningDisposition.Evaluated, expectedCandidate: 0);
    }

    [Fact]
    public async Task Legacy_unknown_partial_removal_stays_held_without_commit_or_credit()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var source = await SeedLegacyUnknownOrderAsync(session.ServiceSessionId);
        var quote = await Client.PostAsJsonAsync($"/api/staff/orders/{source.OrderId}/amendments/quote",
            new OrderAmendmentQuoteRequest
            {
                ExpectedOrderVersion = source.Version,
                ExpectedAccountRevision = source.AccountRevision,
                Reason = "Guest removed an item before service",
                ReviewAcknowledged = true,
                PreparingOverrideAcknowledged = false,
                ReleaseAdditionsToKitchen = false,
                LocalProviderSupplementConsent = false,
                Changes =
                [
                    new OrderAmendmentLineChangeRequest
                    {
                        OrderItemId = source.OrderItemId,
                        Kind = OrderAmendmentChangeKind.Void,
                        StartOrdinal = 1,
                        Quantity = 1
                    }
                ]
            }, JsonOptions);
        var quoteBody = await quote.Content.ReadAsStringAsync();

        quote.StatusCode.Should().Be(HttpStatusCode.OK, quoteBody);
        var quoteData = JsonSerializer.Deserialize<ApiResponse<OrderAmendmentQuoteDto>>(quoteBody, JsonOptions)!.Data!;
        quoteData.FinancialPreview.ResolutionStatus.Should().Be(OrderAmendmentFinancialResolutionStatus.Pending);
        quoteData.FinancialPreview.RemovedUnitValueMinor.Should().Be(500,
            "one unit is removed from the two-unit legacy order");
        using var commit = await Client.PostAsJsonAsync(
            $"/api/staff/orders/{source.OrderId}/amendments/commit", new OrderAmendmentCommitRequest
            {
                AmendmentId = quoteData.AmendmentId,
                ClientOperationId = Guid.NewGuid(),
                ExpectedOrderVersion = quoteData.ExpectedOrderVersion,
                ExpectedAccountRevision = quoteData.ExpectedAccountRevision,
                ReviewAcknowledged = true
            }, JsonOptions);
        var commitBody = await commit.Content.ReadAsStringAsync();
        commit.StatusCode.Should().Be(HttpStatusCode.Conflict, commitBody);
        commitBody.Should().Contain("accepted loyalty evaluation is pending");
        await using var verify = DatabaseFixture.CreateContext();
        var snapshot = await verify.OrderBillingSnapshots.SingleAsync(value => value.OrderId == source.OrderId);
        snapshot.EarningDisposition.Should().BeNull("this fixture represents an untouched legacy row");
        snapshot.EffectiveEarningDisposition.Should().Be(OrderBillingEarningDisposition.Unevaluated);
        snapshot.EarnedPointsCandidate.Should().BeNull();
        var amendment = await verify.OrderAmendments.SingleAsync(value => value.SourceOrderId == source.OrderId);
        amendment.State.Should().NotBe(OrderAmendmentState.Committed);
        (await verify.OrderBillingCredits.CountAsync(value => value.SourceOrderId == source.OrderId)).Should().Be(0);
        (await verify.Orders.Where(value => value.Id == source.OrderId)
            .Select(value => value.BillingCreditAmount).SingleAsync()).Should().Be(0m);
    }

    private async Task CreateAndVoidRoundAsync(
        Guid serviceSessionId, Guid? customerUserId, OrderBillingEarningDisposition expectedDisposition,
        int? expectedCandidate = null)
    {
        var create = await Client.PostAsJsonAsync("/api/staff/orders/round", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = nameof(OrderType.DineIn),
            tableId = _tableId,
            serviceSessionId,
            paymentState = "Unpaid",
            customerUserId,
            items = new[] { new { productId = _productId, quantity = 1 } }
        }, JsonOptions);
        var createBody = await create.Content.ReadAsStringAsync();
        create.StatusCode.Should().Be(HttpStatusCode.OK, createBody);
        var round = JsonSerializer.Deserialize<ApiResponse<OrderDto>>(createBody, JsonOptions)!.Data!;

        int orderVersion;
        long accountRevision;
        Guid orderItemId;
        long expectedCreditMinor;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var source = await context.Orders.Include(value => value.Items)
                .SingleAsync(value => value.Id == round.Id);
            orderVersion = source.Version;
            orderItemId = source.Items.Single(value => !value.ParentOrderItemId.HasValue).Id;
            var acceptedItem = source.Items.Single(value => value.Id == orderItemId);
            acceptedItem.UnitPrice.Should().Be(12.99m, "the seeded Test Pizza has a known price");
            acceptedItem.ItemTotal.Should().Be(12.99m);
            expectedCreditMinor = SeededTestPizzaPriceMinor;
            accountRevision = await context.TableServiceSessions.Where(value => value.Id == serviceSessionId)
                .Select(value => value.AccountRevision).SingleAsync();
            source.UserId.Should().Be(customerUserId);
            source.FidelityPointsEarned.Should().Be(0);

            var accepted = await context.OrderBillingSnapshots.SingleAsync(value => value.OrderId == source.Id);
            accepted.EarningDisposition.Should().Be(expectedDisposition);
            accepted.EffectiveEarningDisposition.Should().Be(expectedDisposition);
            accepted.EarnedPointsCandidate.Should().Be(expectedCandidate);
        }

        var quoteRequest = new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = orderVersion,
            ExpectedAccountRevision = accountRevision,
            Reason = "Guest removed an item before service",
            ReviewAcknowledged = true,
            PreparingOverrideAcknowledged = false,
            ReleaseAdditionsToKitchen = false,
            LocalProviderSupplementConsent = false,
            Changes =
            [
                new OrderAmendmentLineChangeRequest
                {
                    OrderItemId = orderItemId,
                    Kind = OrderAmendmentChangeKind.Void,
                    StartOrdinal = 1,
                    Quantity = 1
                }
            ]
        };
        using var quoteResponse = await Client.PostAsJsonAsync(
            $"/api/staff/orders/{round.Id}/amendments/quote", quoteRequest, JsonOptions);
        var quoteJson = await quoteResponse.Content.ReadAsStringAsync();
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK, quoteJson);
        var quote = JsonSerializer.Deserialize<ApiResponse<OrderAmendmentQuoteDto>>(quoteJson, JsonOptions)!.Data!;
        quote.FinancialPreview.CreditState.Should().Be(OrderAmendmentCreditState.BalanceReduction);
        quote.FinancialPreview.PotentialCreditMinor.Should().Be(expectedCreditMinor);

        using var commitResponse = await Client.PostAsJsonAsync(
            $"/api/staff/orders/{round.Id}/amendments/commit", new OrderAmendmentCommitRequest
            {
                AmendmentId = quote.AmendmentId,
                ClientOperationId = Guid.NewGuid(),
                ExpectedOrderVersion = quote.ExpectedOrderVersion,
                ExpectedAccountRevision = quote.ExpectedAccountRevision,
                ReviewAcknowledged = true
            }, JsonOptions);
        var commitJson = await commitResponse.Content.ReadAsStringAsync();
        commitResponse.StatusCode.Should().Be(HttpStatusCode.OK, commitJson);
        var committed = JsonSerializer.Deserialize<ApiResponse<OrderAmendmentCommitDto>>(
            commitJson, JsonOptions)!.Data!;
        committed.FinancialResolution.ResolutionStatus.Should().Be(OrderAmendmentFinancialResolutionStatus.Resolved);
        committed.FinancialResolution.CreditState.Should().Be(OrderAmendmentCreditState.BalanceReduction);

        await using var result = DatabaseFixture.CreateContext();
        var storedOrder = await result.Orders.SingleAsync(value => value.Id == round.Id);
        storedOrder.BillingCreditAmount.Should().Be(expectedCreditMinor / 100m);
        storedOrder.FidelityPointsEarned.Should().Be(0);
        (await result.OrderBillingCredits.CountAsync(value => value.SourceOrderId == round.Id)).Should().Be(1);
        (await result.OrderBillingAwardWitnesses.AnyAsync(value => value.OrderId == round.Id)).Should().BeFalse();
        (await result.FidelityPointsTransactions.AnyAsync(value => value.OrderId == round.Id
            && value.TransactionType == TransactionType.Earned)).Should().BeFalse();
        (await result.OrderBillingEarningRetirements.AnyAsync(value => value.OrderId == round.Id)).Should().BeFalse();
    }

    private async Task<LegacySource> SeedLegacyUnknownOrderAsync(Guid serviceSessionId)
    {
        var now = DateTime.UtcNow;
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        var session = await context.TableServiceSessions.SingleAsync(value => value.Id == serviceSessionId);
        session.RecordAccountChange();
        var source = new Order
        {
            Id = orderId,
            OrderNumber = $"LU-{orderId:N}"[..14],
            Type = OrderType.DineIn,
            TableId = _tableId,
            TableNumber = TableNumber,
            ServiceSessionId = serviceSessionId,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            Version = 1,
            SubTotal = 10m,
            Total = 10m,
            TotalPaid = 0m,
            RemainingAmount = 10m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderBillingKnownIneligibleCreditIntegrationTests),
            Items =
            [
                new OrderItem
                {
                    Id = itemId,
                    ProductId = _productId,
                    ProductName = "Test Pizza",
                    Quantity = 2,
                    UnitPrice = 5m,
                    ItemTotal = 10m,
                    CreatedBy = nameof(OrderBillingKnownIneligibleCreditIntegrationTests)
                }
            ]
        };
        context.Orders.Add(source);
        await context.SaveChangesAsync();

        var snapshot = OrderBillingSnapshotFactory.Build(source, "CHF", null, null, 1000);
        snapshot.Header.EarningDisposition = null;
        context.OrderBillingSnapshots.Add(snapshot.Header);
        context.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
        context.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);
        await context.SaveChangesAsync();
        return new(orderId, itemId, source.Version, session.AccountRevision);
    }

    private async Task<TableServiceSessionDto> OpenSessionAsync()
    {
        using var response = await Client.PostAsJsonAsync(
            "/api/table-service-sessions", new { tableId = _tableId, currency = "CHF" }, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<ApiResponse<TableServiceSessionDto>>(body, JsonOptions)!.Data!;
    }

    private sealed class MutableTestTenantModules : ITenantModules
    {
        public bool LoyaltyEnabled { get; set; }

        public bool IsEnforced => true;

        public IReadOnlyList<string> EnabledModules => LoyaltyEnabled
            ? [ModuleIds.Core, ModuleIds.Server, ModuleIds.Loyalty]
            : [ModuleIds.Core, ModuleIds.Server];

        public bool IsEnabled(string moduleId) => moduleId switch
        {
            ModuleIds.Core or ModuleIds.Server => true,
            ModuleIds.Loyalty => LoyaltyEnabled,
            _ => false
        };
    }

    private sealed record LegacySource(Guid OrderId, Guid OrderItemId, int Version, long AccountRevision);
}
