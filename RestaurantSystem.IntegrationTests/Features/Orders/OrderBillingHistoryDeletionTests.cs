using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Commands.DeleteOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class OrderBillingHistoryDeletionTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Deletion_preserves_credited_sources_and_committed_supplements(bool credit, bool deleteSupplement)
    {
        var sessionId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        Guid orderId;
        await using (var seed = fixture.CreateContext())
        {
            seed.TableServiceSessions.Add(new TableServiceSession
            { Id = sessionId, Currency = "CHF", CreatedBy = "test" });
            var source = NewOrder(sessionId, 10m);
            var item = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = source.Id,
                Quantity = 1,
                ItemTotal = 10m,
                UnitPrice = 10m,
                ProductName = "Source dish",
                CreatedBy = "test"
            };
            source.Items.Add(item);
            var supplement = deleteSupplement ? NewOrder(sessionId, 12m) : null;
            var snapshot = new OrderItemDto { Id = item.Id, Quantity = 1, ItemTotal = 10m, UnitPrice = 10m };
            var amendment = new OrderAmendment
            {
                Id = Guid.NewGuid(),
                SourceOrderId = source.Id,
                ServiceSessionId = sessionId,
                SupplementOrderId = supplement?.Id,
                ActorUserId = actorId,
                ActorRole = "Cashier",
                State = OrderAmendmentState.Committed,
                ExpectedOrderVersion = 1,
                PayloadHash = new string('d', 64),
                ExpiresAt = DateTime.UtcNow,
                RequestJson = "{}",
                ChangesJson = credit ? OrderAmendmentJson.Serialize(new[]
                    { new OrderAmendmentChangeSnapshot(item.Id, OrderAmendmentChangeKind.Void, 1, 1, false, snapshot, null) }) : "[]",
                SourceSnapshotJson = OrderAmendmentJson.Serialize(new OrderAmendmentSourceSnapshot(
                    source.Id, source.OrderNumber, source.Type, source.Status, source.IsKitchenReleased,
                    sessionId, source.Version, "CHF", source.Total, [snapshot])),
                FinancialResolutionJson = OrderAmendmentJson.Serialize(new OrderAmendmentFinancialPreviewDto(
                    "CHF", supplement is null ? 0 : 1200, credit ? 1000 : 0, credit ? -1000 : supplement is null ? 0 : 1200,
                    credit ? 1000 : 0, credit ? OrderAmendmentFinancialResolutionStatus.Resolved
                        : OrderAmendmentFinancialResolutionStatus.NotRequired,
                    credit ? OrderAmendmentCreditState.BalanceReduction : OrderAmendmentCreditState.None,
                    OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.None)),
                CreatedBy = "test"
            };
            seed.Orders.Add(source);
            if (supplement is not null) seed.Orders.Add(supplement);
            seed.OrderAmendments.Add(amendment);
            if (credit)
            {
                source.BillingCreditAmount = 10m;
                source.RemainingAmount = 0m;
                seed.OrderBillingCredits.Add(new OrderBillingCredit
                {
                    Id = Guid.NewGuid(),
                    SourceOrderId = source.Id,
                    AmendmentId = amendment.Id,
                    AmountMinor = 1000,
                    Currency = "CHF",
                    ActorUserId = actorId,
                    ActorRole = "Cashier",
                    CreatedBy = "test"
                });
            }
            await seed.SaveChangesAsync();
            orderId = supplement?.Id ?? source.Id;
        }

        await using (var context = fixture.CreateContext())
        {
            var handler = new DeleteOrderCommandHandler(context, Mock.Of<ICurrentUserService>(),
                NullLogger<DeleteOrderCommandHandler>.Instance);
            var remove = () => handler.Handle(new DeleteOrderCommand(orderId), CancellationToken.None);
            await remove.Should().ThrowAsync<ConflictException>().WithMessage("*cannot be deleted*");
        }
        await using var verify = fixture.CreateContext();
        (await verify.Orders.AnyAsync(order => order.Id == orderId)).Should().BeTrue();
        (await verify.OrderAmendments.CountAsync()).Should().Be(1);
        (await verify.OrderBillingCredits.CountAsync()).Should().Be(credit ? 1 : 0);
        var read = () => new AccountDebtSnapshotReader(verify).ReadAsync(sessionId, CancellationToken.None);
        await read.Should().NotThrowAsync("the retained account must remain readable after a refused delete");
    }

    private static Order NewOrder(Guid sessionId, decimal total) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = $"DELETE-{Guid.NewGuid():N}"[..20],
        Type = OrderType.DineIn,
        Status = OrderStatus.Confirmed,
        ServiceSessionId = sessionId,
        SubTotal = total,
        Total = total,
        RemainingAmount = total,
        CreatedBy = "test"
    };
}
