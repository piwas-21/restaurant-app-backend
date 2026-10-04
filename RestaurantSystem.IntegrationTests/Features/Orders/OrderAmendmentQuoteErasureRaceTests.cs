using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class OrderAmendmentQuoteErasureRaceTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quote_only_persists_when_its_order_version_still_matches_after_preview(bool eraseDuringPreview)
    {
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderNumber = $"QUOTE-{Guid.NewGuid():N}"[..20],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 12.35m,
            Total = 12.35m,
            RemainingAmount = 12.35m,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "quote-erasure-test",
            Items = [new OrderItem
            {
                Id = itemId, ProductName = "Retained dish", Quantity = 1, UnitPrice = 12.35m,
                ItemTotal = 12.35m, SpecialInstructions = "Customer private instructions",
                CreatedBy = "quote-erasure-test"
            }]
        };
        await using (var seed = DatabaseFixture.CreateContext())
        {
            await TestUserSeeder.SeedUserAsync(seed, userId);
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
        }

        var request = new OrderAmendmentQuoteRequest
        {
            ExpectedOrderVersion = order.Version,
            ReviewAcknowledged = true,
            Reason = "Customer private amendment reason",
            Changes = [new OrderAmendmentLineChangeRequest
            {
                OrderItemId = itemId, Kind = OrderAmendmentChangeKind.Void, StartOrdinal = 1, Quantity = 1
            }]
        };
        await using var context = DatabaseFixture.CreateContext();
        var preview = new FrozenPreview(async () =>
        {
            if (!eraseDuringPreview) return;
            await using var erasure = DatabaseFixture.CreateContext();
            await using var transaction = await erasure.Database.BeginTransactionAsync();
            await new RetainedCustomerDataScrubber(erasure).ScrubAsync(userId, CancellationToken.None);
            await erasure.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        });
        var actorId = Guid.NewGuid();
        var actor = Mock.Of<ICurrentUserService>(value => value.IsAuthenticated
            && value.UserId == actorId && value.Role == UserRole.Admin && value.IsAdmin);
        var resolver = Mock.Of<IOrderDisplayCurrencyResolver>(value => value.Resolve(It.IsAny<Order>()) == "CHF");
        var financial = new OrderAmendmentFinancialResolutionService(
            resolver, Mock.Of<IOrderBillingAdjustmentWriter>());
        var service = new OrderAmendmentQuoteService(context,
            Mock.Of<ITenantFeatures>(value => value.OrderAmendmentsV1), actor, preview, resolver,
            financial, Options.Create(new OrderAmendmentResolutionSettings()));

        if (eraseDuringPreview)
            await Assert.ThrowsAsync<ConflictException>(() => service.QuoteAsync(order.Id, request, CancellationToken.None));
        else
            await service.QuoteAsync(order.Id, request, CancellationToken.None);

        await using var verify = DatabaseFixture.CreateContext();
        var saved = await verify.OrderAmendments.Where(value => value.SourceOrderId == order.Id).ToListAsync();
        Assert.Equal(eraseDuringPreview ? 0 : 1, saved.Count);
        if (eraseDuringPreview)
        {
            var retained = await verify.Orders.SingleAsync(value => value.Id == order.Id);
            Assert.Null(retained.UserId);
            Assert.True(retained.Version > request.ExpectedOrderVersion);
            Assert.Null((await verify.OrderItems.SingleAsync(value => value.Id == itemId)).SpecialInstructions);
        }
        else
            Assert.Contains("Customer private instructions", saved[0].SourceSnapshotJson);
    }

    private sealed class FrozenPreview(Func<Task> afterRead) : IOrderAmendmentQuotePreviewBuilder
    {
        public Task<OrderDto> MapSourceAsync(Order source, CancellationToken cancellationToken) =>
            Task.FromResult(new OrderDto
            {
                Id = source.Id,
                OrderNumber = source.OrderNumber,
                Type = source.Type.ToString(),
                Status = source.Status.ToString(),
                Version = source.Version,
                Total = source.Total,
                Currency = "CHF",
                Items = source.Items.Select(value => new OrderItemDto
                {
                    Id = value.Id,
                    ProductName = value.ProductName,
                    Quantity = value.Quantity,
                    UnitPrice = value.UnitPrice,
                    ItemTotal = value.ItemTotal,
                    SpecialInstructions = value.SpecialInstructions
                }).ToList()
            });

        public async Task<OrderAmendmentQuotePreview> BuildAsync(Order source, OrderDto sourceDto,
            OrderAmendmentQuoteRequest request, CancellationToken cancellationToken)
        {
            await afterRead();
            return new OrderAmendmentQuotePreview(null, null,
                [new OrderAmendmentChangeSnapshot(sourceDto.Items[0].Id, OrderAmendmentChangeKind.Void,
                    1, 1, true, sourceDto.Items[0], null)]);
        }
    }
}
