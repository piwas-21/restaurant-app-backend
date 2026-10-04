using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RestaurantSystem.Api.BackgroundServices;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.User.Commands.DeleteUserCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.User;

[Collection("Database Lane 3")]
public sealed class OrderBillingSnapshotErasureIntegrationTests(DatabaseFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime SourceCreatedAt = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Scheduled_cleanup_erases_owner_link_and_retains_financial_snapshot()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            await TestUserSeeder.SeedUserAsync(seed, userId);
            await SaveSnapshotAsync(seed, userId, orderId, transactionId);
            var user = await seed.Users.SingleAsync(candidate => candidate.Id == userId);
            user.DeletionScheduledAt = DateTime.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync();
        }

        var cleanup = new AccountCleanupService(
            Factory.Services, NullLogger<AccountCleanupService>.Instance);
        await cleanup.ProcessDeletionRequests(CancellationToken.None);

        await AssertErasedSnapshotAsync(userId, orderId, transactionId);
    }

    [Fact]
    public async Task Confirmed_customer_deletion_allows_explicit_debit_delete_before_user_delete()
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = NewUser();
        var create = await userManager.CreateAsync(user, "Str0ng!Passw0rd");
        Assert.True(create.Succeeded, string.Join("; ", create.Errors.Select(error => error.Description)));
        var token = await userManager.GenerateUserTokenAsync(user, "Default", "AccountDeletion");

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        await SaveSnapshotAsync(context, user.Id, orderId, transactionId);

        using var response = await PostAsJsonAsync("/api/user/confirm-deletion",
            new { UserId = user.Id, Token = token });
        response.EnsureSuccessStatusCode();
        var result = await ReadResponseAsync<ApiResponse<string>>(response);
        result!.Success.Should().BeTrue(result.Message);

        await AssertErasedSnapshotAsync(user.Id, orderId, transactionId);
    }

    [Fact]
    public async Task Confirmation_waits_for_snapshot_capture_before_deleting_the_redemption_source()
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = NewUser();
        var create = await userManager.CreateAsync(user, "Str0ng!Passw0rd");
        Assert.True(create.Succeeded, string.Join("; ", create.Errors.Select(error => error.Description)));
        var token = await userManager.GenerateUserTokenAsync(user, "Default", "AccountDeletion");

        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductName = "Concurrent retained dish",
            Quantity = 1,
            UnitPrice = 1m,
            ItemTotal = 1m,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-erasure-test"
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"RACE-{Guid.NewGuid():N}"[..17],
            UserId = user.Id,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 1m,
            Total = 0.75m,
            RemainingAmount = 0.75m,
            FidelityPointsRedeemed = 25,
            FidelityPointsDiscount = 0.25m,
            OrderDate = SourceCreatedAt,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-erasure-test",
            Items = [item]
        };
        var debit = new FidelityPointsTransaction
        {
            Id = transactionId,
            UserId = user.Id,
            OrderId = orderId,
            TransactionType = TransactionType.Redeemed,
            Points = -25,
            OrderTotal = null,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-erasure-test"
        };
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Orders.Add(order);
            seed.FidelityPointsTransactions.Add(debit);
            await seed.SaveChangesAsync();
        }

        var snapshot = OrderBillingSnapshotFactory.Build(order, "CHF", null,
            new OrderBillingRedemptionEvidence(transactionId, user.Id, orderId,
                TransactionType.Redeemed, -25, 0.25m, null, SourceCreatedAt), 1_000);
        await using var capture = DatabaseFixture.CreateContext();
        await capture.Database.OpenConnectionAsync();
        await using var captureTransaction = await capture.Database.BeginTransactionAsync();
        await using (var lockOrder = capture.Database.GetDbConnection().CreateCommand())
        {
            lockOrder.CommandText = "SELECT id FROM orders WHERE id = @order_id FOR SHARE";
            lockOrder.Transaction = capture.Database.CurrentTransaction!.GetDbTransaction();
            var orderParameter = lockOrder.CreateParameter();
            orderParameter.ParameterName = "order_id";
            orderParameter.Value = orderId;
            lockOrder.Parameters.Add(orderParameter);
            (await lockOrder.ExecuteScalarAsync()).Should().Be(orderId);
        }
        var captureProcessId = ((NpgsqlConnection)capture.Database.GetDbConnection()).ProcessID;
        capture.OrderBillingSnapshots.Add(snapshot.Header);
        capture.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
        await capture.SaveChangesAsync();

        using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = Client.PostAsJsonAsync("/api/user/confirm-deletion",
            new { UserId = user.Id, Token = token }, requestTimeout.Token);
        var confirmationWaitedForOrder = await WaitForBlockedSessionAsync(captureProcessId);

        capture.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);
        await capture.SaveChangesAsync();
        await captureTransaction.CommitAsync();

        using var response = await request.WaitAsync(TimeSpan.FromSeconds(15));
        response.EnsureSuccessStatusCode();
        var result = await ReadResponseAsync<ApiResponse<string>>(response);
        result!.Success.Should().BeTrue(result.Message);
        confirmationWaitedForOrder.Should().BeTrue(
            "confirmation must reach retained-order scrubbing before deleting the locked redemption row");
        await AssertErasedSnapshotAsync(user.Id, orderId, transactionId);
    }

    [Fact]
    public async Task Admin_delete_handler_erases_owner_link_and_retains_financial_snapshot()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        await TestUserSeeder.SeedUserAsync(context, userId);
        var user = await context.Users.SingleAsync(candidate => candidate.Id == userId);
        user.Role = UserRole.Admin;
        await context.SaveChangesAsync();
        await SaveSnapshotAsync(context, userId, orderId, transactionId);

        var handler = new DeleteUserCommandHandler(context, Mock.Of<ICurrentUserService>(),
            new RetainedCustomerDataScrubber(context), NullLogger<DeleteUserCommandHandler>.Instance);
        var result = await handler.Handle(new DeleteUserCommand(userId), CancellationToken.None);

        result.Success.Should().BeTrue();
        await AssertErasedSnapshotAsync(userId, orderId, transactionId);
    }

    private static ApplicationUser NewUser()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"snapshot-erasure-{suffix}@test.local",
            Email = $"snapshot-erasure-{suffix}@test.local",
            FirstName = "Snapshot",
            LastName = "Erasure",
            Role = UserRole.Customer,
            RefreshToken = string.Empty,
            CreatedBy = "snapshot-erasure-test"
        };
    }

    private static async Task SaveSnapshotAsync(
        ApplicationDbContext context, Guid userId, Guid orderId, Guid transactionId)
    {
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductName = "Retained dish",
            Quantity = 1,
            UnitPrice = 1m,
            ItemTotal = 1m,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-erasure-test"
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"ERASE-{Guid.NewGuid():N}"[..18],
            UserId = userId,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 1m,
            Total = 0.75m,
            RemainingAmount = 0.75m,
            FidelityPointsRedeemed = 25,
            FidelityPointsDiscount = 0.25m,
            CustomerName = "Customer name to scrub",
            CustomerEmail = "customer-to-scrub@example.test",
            OrderDate = SourceCreatedAt,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-erasure-test",
            Items = [item]
        };
        var transaction = new FidelityPointsTransaction
        {
            Id = transactionId,
            UserId = userId,
            OrderId = orderId,
            TransactionType = TransactionType.Redeemed,
            Points = -25,
            OrderTotal = null,
            CreatedAt = SourceCreatedAt,
            CreatedBy = "snapshot-erasure-test"
        };

        context.Orders.Add(order);
        context.FidelityPointsTransactions.Add(transaction);
        await context.SaveChangesAsync();

        var result = OrderBillingSnapshotFactory.Build(order, "CHF", null,
            new OrderBillingRedemptionEvidence(transaction.Id, userId, orderId,
                TransactionType.Redeemed, -25, 0.25m, null, SourceCreatedAt), 1_000);
        context.OrderBillingSnapshots.Add(result.Header);
        context.OrderBillingSnapshotUnits.AddRange(result.Units);
        context.OrderBillingSnapshotOwnerLinks.AddRange(result.OwnerLinks);
        await context.SaveChangesAsync();
    }

    private async Task AssertErasedSnapshotAsync(Guid userId, Guid orderId, Guid transactionId)
    {
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Users.IgnoreQueryFilters().AnyAsync(user => user.Id == userId)).Should().BeFalse();
        (await verify.FidelityPointsTransactions.IgnoreQueryFilters()
            .AnyAsync(transaction => transaction.Id == transactionId)).Should().BeFalse();

        var order = await verify.Orders.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == orderId);
        order.UserId.Should().BeNull();
        order.CustomerName.Should().BeNull();
        order.CustomerEmail.Should().BeNull();

        var snapshot = await verify.OrderBillingSnapshots.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == orderId);
        snapshot.RedemptionTransactionId.Should().Be(transactionId);
        snapshot.RedemptionTransactionPoints.Should().Be(-25);
        snapshot.RedemptionTransactionCreatedAt.Should().Be(SourceCreatedAt);
        snapshot.RedemptionDiscountMinor.Should().Be(25);
        (await verify.OrderBillingSnapshotUnits.CountAsync(unit => unit.OrderId == orderId)).Should().Be(1);

        var link = await verify.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == orderId);
        link.UserId.Should().BeNull();
        link.Disposition.Should().Be(OrderBillingSnapshotOwnerDisposition.Erased);
        link.ErasedAt.Should().NotBeNull();
        link.ErasureTransactionId.Should().MatchRegex("^[0-9]{1,20}$");
        link.CreatedBy.Should().Be(OrderBillingSnapshotFactory.SnapshotAuditIdentifier);

        var noOpUpdateCount = await verify.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE orders SET user_id = NULL WHERE id = {orderId}");
        noOpUpdateCount.Should().Be(1,
            "a same-value owner update after the erasure transaction must not be treated as a new unlink");

        var unchangedLink = await verify.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .SingleAsync(candidate => candidate.OrderId == orderId);
        unchangedLink.Disposition.Should().Be(OrderBillingSnapshotOwnerDisposition.Erased);
        unchangedLink.ErasureTransactionId.Should().Be(link.ErasureTransactionId);
        unchangedLink.ErasedAt.Should().Be(link.ErasedAt);
    }

    private async Task<bool> WaitForBlockedSessionAsync(int blockingProcessId)
    {
        await using var observer = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await observer.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity "
            + "WHERE datname = current_database() AND wait_event_type = 'Lock' "
            + "AND @blocking_pid = ANY(pg_blocking_pids(pid)))", observer);
        command.Parameters.AddWithValue("blocking_pid", blockingProcessId);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if ((bool)(await command.ExecuteScalarAsync())!)
                return true;
            await Task.Delay(25);
        }

        return false;
    }
}
