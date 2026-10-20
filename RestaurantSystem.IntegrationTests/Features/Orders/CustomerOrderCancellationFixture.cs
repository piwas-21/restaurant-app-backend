using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public abstract class CustomerOrderCancellationFixture(DatabaseFixture fixture) : CustomerCheckoutAcceptanceFixture(fixture)
{
    protected const string CancellationPath = "/api/customer-mobile/orders/cancellation";
    protected readonly Mock<ICustomerOrderCancellationNotifier> CancellationNotifications = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<ICustomerOrderCancellationNotifier>();
        services.AddSingleton(CancellationNotifications.Object);
    }

    protected async Task<CustomerOrderCancellationGuestDto> AwaitingDelayAsync(bool customer = false)
    {
        if (customer) AuthenticateAsUser();
        var submission = await PreparedAsync(await IntentAsync());
        var result = await OrderResolutionAsync(await SubmitAsync(submission));
        var orderId = result.OrderId!.Value;
        await ChangeAsync(orderId, async (context, order) =>
        {
            order.Status = OrderStatus.PendingApproval;
            // Independent database fixture: only pending/approval transitions ever occurred.
            context.OrderStatusHistories.RemoveRange(await context.OrderStatusHistories.Where(row => row.OrderId == orderId).ToListAsync());
            context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                FromStatus = OrderStatus.Pending,
                ToStatus = OrderStatus.PendingApproval,
                ChangedAt = DateTime.UtcNow,
                ChangedBy = nameof(CustomerOrderCancellationFixture),
                CreatedBy = nameof(CustomerOrderCancellationFixture)
            });
            var configuration = await context.OrderTypeConfigurations.SingleAsync(row => row.OrderType == order.Type);
            configuration.ConfirmationFlow = OrderConfirmationFlows.Direct;
        });
        var proof = new CustomerOrderRecoveryDto
        {
            SchemaVersion = 1,
            OperationId = submission.OperationId,
            PayloadHash = submission.PayloadHash
        };
        var preparation = await DataAsync<CustomerOrderCancellationPreparationDto>(
            await SendAsync(CancellationPath + "/original/prepare", proof));
        preparation.Allowed.Should().BeTrue($"delay fixture should be eligible, reason: {preparation.Reason}");
        return new()
        {
            Proof = proof,
            Intent = new()
            {
                SchemaVersion = 1,
                OperationId = Guid.NewGuid(),
                OrderId = orderId,
                ExpectedVersion = preparation.ExpectedVersion,
                PayloadHash = preparation.PayloadHash!
            }
        };
    }

    protected async Task<CustomerOrderCancellationGuestDto> HistoricalDelayAsync()
    {
        // Independent retained-order fixture without a billing snapshot: immutable native
        // snapshots cannot be rewritten to manufacture hostile scalar financial evidence.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = new Order
        {
            OrderNumber = Guid.NewGuid().ToString("N")[..20],
            Type = OrderType.Takeaway,
            Status = OrderStatus.PendingApproval,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 10,
            Total = 10,
            RemainingAmount = 10,
            OrderDate = DateTime.UtcNow,
            CreatedBy = nameof(CustomerOrderCancellationFixture)
        };
        context.Orders.Add(order);
        (await context.OrderTypeConfigurations.SingleAsync(value => value.OrderType == order.Type))
            .ConfirmationFlow = OrderConfirmationFlows.Direct;
        await context.SaveChangesAsync();
        var proof = new CustomerOrderRecoveryDto { SchemaVersion = 1, OperationId = Guid.NewGuid(), PayloadHash = new string('a', 64) };
        context.Set<CustomerOrderOperation>().Add(new()
        {
            OperationId = proof.OperationId,
            OrderId = order.Id,
            PayloadHash = proof.PayloadHash,
            OwnerHash = CustomerBasketOperationHash.Owner(Guid.Parse(Session), null),
            CompletedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var preparation = await DataAsync<CustomerOrderCancellationPreparationDto>(await SendAsync(CancellationPath + "/original/prepare", proof));
        preparation.Allowed.Should().BeTrue();
        return new()
        {
            Proof = proof,
            Intent = new()
            {
                SchemaVersion = 1,
                OperationId = Guid.NewGuid(),
                OrderId = order.Id,
                ExpectedVersion = order.Version,
                PayloadHash = CustomerOrderCancellationHash.Payload(order.Id, order.Version)
            }
        };
    }

    protected async Task ChangeAsync(Guid orderId, Func<ApplicationDbContext, Order, Task> change)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await change(context, await context.Orders.SingleAsync(order => order.Id == orderId));
        await context.SaveChangesAsync();
    }

    protected async Task<CustomerOrderCancellationResolutionDto> CancelAsync(CustomerOrderCancellationGuestDto request) =>
        await DataAsync<CustomerOrderCancellationResolutionDto>(await SendAsync(CancellationPath + "/original/submit", request));

    protected static async Task<T> DataAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("customerEmail").And.NotContain("guestStatusToken").And.NotContain("ownerHash")
            .And.NotContain("quickActionToken").And.NotContain("customerPhone").And.NotContain("paymentIntentId");
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;
    }

    protected async Task<List<CustomerOrderCancellationOperation>> DecisionsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Set<CustomerOrderCancellationOperation>().AsNoTracking().ToListAsync();
    }
}
