using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableServiceSessions;

[Collection("Database Lane 4")]
public sealed class TableReadinessLegacyDebtTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Local_legacy_paid_debt_does_not_block_open_from_stale_remaining_cache(bool readinessEnabled)
    {
        await using var context = fixture.CreateContext();
        var table = NewTable();
        context.Tables.Add(table);
        context.Orders.Add(NewOrder(table.Id, paid: true));
        await context.SaveChangesAsync();
        var identity = await new TableIdentityResolver(context).ResolveActiveAsync(table.Id, null, CancellationToken.None);
        var result = await TableServiceSessionOpenHelpers.ValidateCanCreateAsync(
            context, table, identity, 0.01m, readinessEnabled, CancellationToken.None);
        result.Should().BeNull("a literal CHF 10 captured payment settles the CHF 10 legacy order");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Local_legacy_unpaid_debt_blocks_open_despite_zero_remaining_cache(bool readinessEnabled)
    {
        await using var context = fixture.CreateContext();
        var table = NewTable();
        context.Tables.Add(table);
        context.Orders.Add(NewOrder(table.Id, paid: false));
        await context.SaveChangesAsync();
        var identity = await new TableIdentityResolver(context).ResolveActiveAsync(table.Id, null, CancellationToken.None);
        var result = await TableServiceSessionOpenHelpers.ValidateCanCreateAsync(
            context, table, identity, 0.01m, readinessEnabled, CancellationToken.None);
        result.Should().NotBeNull();
        result!.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionAmbiguous);
    }

    private static Table NewTable() => new()
    {
        Id = Guid.NewGuid(),
        TableNumber = "DEBT-QA",
        MaxGuests = 4,
        ReadinessState = TableReadinessState.ReadyForGuests,
        CreatedBy = nameof(TableReadinessLegacyDebtTests)
    };

    private static Order NewOrder(Guid tableId, bool paid)
    {
        var order = new Order
        {
            TableId = tableId,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            OrderNumber = "LEGACY-DEBT",
            Total = 10m,
            TotalPaid = paid ? 10m : 0m,
            RemainingAmount = paid ? 99m : 0m,
            PaymentStatus = paid ? PaymentStatus.Completed : PaymentStatus.Pending,
            OrderDate = DateTime.UtcNow,
            CreatedBy = nameof(TableReadinessLegacyDebtTests)
        };
        if (paid) order.Payments.Add(new OrderPayment
        {
            Amount = 10m,
            Currency = "CHF",
            PaymentMethod = PaymentMethod.Cash,
            Status = PaymentStatus.Completed,
            PaymentDate = DateTime.UtcNow,
            TransactionId = "ready-local-debt",
            CreatedBy = nameof(TableReadinessLegacyDebtTests)
        });
        return order;
    }
}
