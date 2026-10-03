using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class OrderBillingCreditPersistenceTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const int TableNumber = 73;
    private Guid _tableId;
    private Guid _productId;
    private Guid _sourceOrderId;
    private Guid _sourceItemId;

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<TenantFeatureSettings>(settings => settings.OrderAmendmentsV1 = true);

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products.Where(product => product.Name == "Test Pizza")
            .Select(product => product.Id).SingleAsync();
        _tableId = Guid.NewGuid();
        context.Tables.Add(new Table
        {
            Id = _tableId,
            TableNumber = TableNumber.ToString(),
            MaxGuests = 4,
            IsActive = true,
            CreatedBy = nameof(OrderBillingCreditPersistenceTests)
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Amendment_credit_replays_once_and_native_bill_payment_clears_effective_account()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        session.Currency.Should().Be("CHF");
        await SeedSourceOrderAsync(session.ServiceSessionId);

        var quote = await PostApiAsync<OrderAmendmentQuoteRequest, OrderAmendmentQuoteDto>(
            $"/api/staff/orders/{_sourceOrderId}/amendments/quote",
            new OrderAmendmentQuoteRequest
            {
                ExpectedOrderVersion = await ReadOrderVersionAsync(),
                ExpectedAccountRevision = session.AccountRevision,
                Reason = "Guest removed the food item",
                ReviewAcknowledged = true,
                PreparingOverrideAcknowledged = false,
                ReleaseAdditionsToKitchen = false,
                LocalProviderSupplementConsent = false,
                Changes =
                [
                    new OrderAmendmentLineChangeRequest
                    {
                        OrderItemId = _sourceItemId,
                        Kind = OrderAmendmentChangeKind.Void,
                        StartOrdinal = 1,
                        Quantity = 1
                    }
                ]
            });

        quote.Success.Should().BeTrue(Describe(quote));
        quote.Data!.FinancialPreview.Currency.Should().Be("CHF");
        quote.Data.FinancialPreview.RemovedUnitValueMinor.Should().Be(1000);
        quote.Data.FinancialPreview.PotentialCreditMinor.Should().Be(1000);
        quote.Data.FinancialPreview.CreditState.Should().Be(OrderAmendmentCreditState.BalanceReduction);

        var commitRequest = new OrderAmendmentCommitRequest
        {
            AmendmentId = quote.Data.AmendmentId,
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = quote.Data.ExpectedOrderVersion,
            ExpectedAccountRevision = quote.Data.ExpectedAccountRevision,
            ReviewAcknowledged = true
        };
        var committed = await PostApiAsync<OrderAmendmentCommitRequest, OrderAmendmentCommitDto>(
            $"/api/staff/orders/{_sourceOrderId}/amendments/commit", commitRequest);
        committed.Success.Should().BeTrue(Describe(committed));
        committed.Data!.FinancialResolution.ResolutionStatus
            .Should().Be(OrderAmendmentFinancialResolutionStatus.Resolved);

        await using (var creditedAccount = DatabaseFixture.CreateContext())
        {
            (await new AccountDebtSnapshotReader(creditedAccount)
                .ReadAsync(session.ServiceSessionId, CancellationToken.None))
                .Debt.OutstandingMinor.Should().Be(500, "the credit removes food while retaining the 2 + 3 charge");
        }

        AuthenticateAsRole(UserRole.Cashier);
        var paymentOperationId = Guid.NewGuid();
        var paid = await PostApiAsync<AddTableBillPaymentCommand, TableBillDto>(
            $"/api/orders/table/{TableNumber}/bill/payments",
            new AddTableBillPaymentCommand
            {
                TableNumber = TableNumber,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 5m,
                Currency = session.Currency,
                OperationId = paymentOperationId
            });
        paid.Success.Should().BeTrue(Describe(paid));
        paid.Data!.Total.Should().Be(5m, "the bill carries only retained tip and fee after the food credit");
        paid.Data.TotalPaid.Should().Be(5m);
        paid.Data.Remaining.Should().Be(0m);

        var replay = await PostApiAsync<OrderAmendmentCommitRequest, OrderAmendmentCommitDto>(
            $"/api/staff/orders/{_sourceOrderId}/amendments/commit", commitRequest);
        replay.Success.Should().BeTrue(Describe(replay));
        replay.Data.Should().BeEquivalentTo(committed.Data, "the same client operation returns its frozen result");

        await using (var verify = DatabaseFixture.CreateContext())
        {
            var source = await verify.Orders.Include(order => order.Payments)
                .SingleAsync(order => order.Id == _sourceOrderId);
            source.Total.Should().Be(15m, "the original order charge remains immutable");
            source.Tip.Should().Be(2m);
            source.DeliveryFee.Should().Be(3m);
            source.BillingCreditAmount.Should().Be(10m);
            source.PayableTotal.Should().Be(5m);
            source.TotalPaid.Should().Be(5m);
            source.RemainingAmount.Should().Be(0m);
            source.PaymentStatus.Should().Be(PaymentStatus.Completed);
            source.Payments.Should().ContainSingle(payment =>
                payment.PaymentMethod == PaymentMethod.Cash
                && payment.Status == PaymentStatus.Completed
                && payment.Amount == 5m
                && payment.Currency == session.Currency);

            var credit = await verify.OrderBillingCredits.SingleAsync(value => value.SourceOrderId == source.Id);
            credit.AmendmentId.Should().Be(quote.Data.AmendmentId);
            credit.AmountMinor.Should().Be(1000);
            credit.Currency.Should().Be(session.Currency);
            credit.ActorUserId.Should().Be(Guid.Parse(TestAuthHandler.StaffUserId));
            credit.ActorRole.Should().Be(UserRole.Server.ToString());
            (await verify.OrderBillingCredits.CountAsync()).Should().Be(1);
            (await verify.TableBillPaymentOperations.CountAsync()).Should().Be(1);
            (await verify.TableBillPaymentOperations.SingleAsync()).Amount.Should().Be(5m);
            (await verify.TableServiceSessions.SingleAsync(value => value.Id == session.ServiceSessionId))
                .BillingAllocationVersion.Should().Be(1);
        }

        await using var accountContext = DatabaseFixture.CreateContext();
        (await new AccountDebtSnapshotReader(accountContext)
            .ReadAsync(session.ServiceSessionId, CancellationToken.None))
            .Debt.OutstandingMinor.Should().Be(0, "the persisted account projection agrees with the payable order balance");

        await AssertTriggerRejectedAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE order_billing_credits SET amount_minor = amount_minor + 1 WHERE source_order_id = {_sourceOrderId}");
        });
        await AssertTriggerRejectedAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM order_billing_credits WHERE source_order_id = {_sourceOrderId}");
        });
        await AssertTriggerRejectedAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE table_service_sessions SET billing_allocation_version = 0 WHERE id = {session.ServiceSessionId}");
        });
    }

    private async Task<TableServiceSessionDto> OpenSessionAsync()
    {
        using var response = await Client.PostAsJsonAsync(
            "/api/table-service-sessions", new { tableId = _tableId, currency = "CHF" }, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<ApiResponse<TableServiceSessionDto>>(body, JsonOptions)!.Data!;
    }

    private async Task SeedSourceOrderAsync(Guid serviceSessionId)
    {
        _sourceOrderId = Guid.NewGuid();
        _sourceItemId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var context = DatabaseFixture.CreateContext();
        var source = new Order
        {
            Id = _sourceOrderId,
            OrderNumber = $"BC-{Guid.NewGuid():N}"[..14],
            Type = OrderType.DineIn,
            TableId = _tableId,
            TableNumber = TableNumber,
            ServiceSessionId = serviceSessionId,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            Version = 1,
            SubTotal = 10m,
            Tip = 2m,
            DeliveryFee = 3m,
            Total = 15m,
            TotalPaid = 0m,
            RemainingAmount = 15m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderBillingCreditPersistenceTests),
            Items =
            [
                new OrderItem
                {
                    Id = _sourceItemId,
                    ProductId = _productId,
                    ProductName = "Test Pizza",
                    Quantity = 1,
                    UnitPrice = 10m,
                    ItemTotal = 10m,
                    CreatedBy = nameof(OrderBillingCreditPersistenceTests)
                }
            ]
        };
        context.Orders.Add(source);
        await context.SaveChangesAsync();
    }

    private async Task<int> ReadOrderVersionAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.Orders.Where(order => order.Id == _sourceOrderId)
            .Select(order => order.Version).SingleAsync();
    }

    private async Task<ApiResponse<TResponse>> PostApiAsync<TRequest, TResponse>(string path, TRequest request)
    {
        using var response = await Client.PostAsJsonAsync(path, request, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<ApiResponse<TResponse>>(body, JsonOptions)!;
    }

    private async Task AssertTriggerRejectedAsync(Func<ApplicationDbContext, Task> write)
    {
        await using var context = DatabaseFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => write(context));
        exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    private static string Describe<T>(ApiResponse<T> response) =>
        response.Errors is { Count: > 0 } ? string.Join("; ", response.Errors) : response.Message ?? "";
}
