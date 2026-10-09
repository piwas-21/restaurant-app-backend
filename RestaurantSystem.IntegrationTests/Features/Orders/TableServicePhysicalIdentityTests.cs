using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.ClearPendingTableOrdersCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.ReleaseTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class TableServicePhysicalIdentityTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unresolved_legacy_visit_cannot_be_freed_but_remains_payable_and_intact()
    {
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using (var seed = fixture.CreateContext())
        {
            seed.TableServiceSessions.Add(new TableServiceSession
            {
                Id = sessionId,
                TableNumber = 58,
                Status = TableServiceSessionStatus.Open,
                Version = 1,
                OpenedAt = now,
                CreatedAt = now,
                CreatedBy = nameof(TableServicePhysicalIdentityTests)
            });
            seed.Orders.Add(new Order
            {
                Id = orderId,
                OrderNumber = $"IDENTITY-{Guid.NewGuid():N}"[..17],
                Type = OrderType.DineIn,
                TableNumber = 58,
                ServiceSessionId = sessionId,
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
                Total = 12m,
                RemainingAmount = 12m,
                OrderDate = now,
                CreatedAt = now,
                CreatedBy = nameof(TableServicePhysicalIdentityTests)
            });
            await seed.SaveChangesAsync();
        }

        await using (var clearContext = fixture.CreateContext())
        {
            var currentUser = Mock.Of<ICurrentUserService>(value =>
                value.GetAuditIdentifier() == nameof(TableServicePhysicalIdentityTests));
            var result = await new ClearPendingTableOrdersCommandHandler(
                clearContext, currentUser, new TableGuestVisitRevoker(clearContext), TimeProvider.System)
                .Handle(new ClearPendingTableOrdersCommand
                {
                    ServiceSessionId = sessionId,
                    ExpectedVersion = 1
                }, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionNotClosable);
        }

        await using (var releaseContext = fixture.CreateContext())
        {
            var currentUser = Mock.Of<ICurrentUserService>(value =>
                value.GetAuditIdentifier() == nameof(TableServicePhysicalIdentityTests));
            var result = await new ReleaseTableServiceSessionCommandHandler(
                releaseContext,
                SessionReader(releaseContext),
                new TableGuestVisitRevoker(releaseContext),
                currentUser).Handle(new ReleaseTableServiceSessionCommand
                {
                    ServiceSessionId = sessionId,
                    ExpectedVersion = 1
                }, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionNotClosable);
        }

        await using var verify = fixture.CreateContext();
        (await verify.TableServiceSessions.SingleAsync(value => value.Id == sessionId)).Should().Match(
            (TableServiceSession value) => value.Status == TableServiceSessionStatus.Open
                && value.Version == 1 && value.ReleasedAt == null);
        (await verify.Orders.SingleAsync(value => value.Id == orderId)).Should().Match(
            (Order value) => value.Status == OrderStatus.Pending && value.ServiceSessionId == sessionId);
    }

    private static TableServiceSessionReader SessionReader(ApplicationDbContext context)
    {
        var mapping = new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
            NullLogger<OrderMappingService>.Instance);
        var assembler = new TableBillAssembler(context, mapping, NullLogger<TableBillAssembler>.Instance);
        return new TableServiceSessionReader(context, assembler);
    }
}
