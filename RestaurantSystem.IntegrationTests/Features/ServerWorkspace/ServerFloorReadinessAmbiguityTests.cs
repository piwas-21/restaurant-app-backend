using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.ServerWorkspace;

[Collection("Database Lane 4")]
public sealed class ServerFloorReadinessAmbiguityTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private Guid _tableId;

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<TenantFeatureSettings>(settings =>
        {
            settings.ServerWorkspaceV2 = true;
            settings.TableGuestVisitsV1 = true;
            settings.TableVisitReadinessV1 = true;
        });

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var table = new Table
        {
            TableNumber = "READY-QA",
            MaxGuests = 4,
            IsActive = true,
            ReadinessState = TableReadinessState.NeedsReset,
            ReadinessVersion = 1,
            CreatedBy = nameof(ServerFloorReadinessAmbiguityTests)
        };
        context.Tables.Add(table);
        await context.SaveChangesAsync();
        _tableId = table.Id;
    }

    [Theory]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Completed)]
    public async Task Unidentified_unpaid_legacy_round_suppresses_ready_without_attributing_its_balance(OrderStatus status)
    {
        await AddUnidentifiedOrderAsync(status, paid: false);
        var table = await ReadTableAsync();
        table.HasLegacyAmbiguity.Should().BeTrue();
        table.State.Should().Be("Ambiguous");
        table.PermittedActions.Should().ContainSingle().Which.Should().Be("ReviewLegacy");
        table.Legacy.Should().BeNull("unidentified money must not be attributed to every table");
        table.Session.Should().BeNull();
    }

    [Fact]
    public async Task Unidentified_open_visit_suppresses_ready_action()
    {
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.TableServiceSessions.Add(new TableServiceSession
            {
                TableId = null,
                TableNumber = null,
                Currency = "CHF",
                Version = 1,
                AccountRevision = 1,
                Status = TableServiceSessionStatus.Open,
                OpenedAt = DateTime.UtcNow,
                CreatedBy = nameof(ServerFloorReadinessAmbiguityTests)
            });
            await context.SaveChangesAsync();
        }
        var table = await ReadTableAsync();
        table.HasLegacyAmbiguity.Should().BeTrue();
        table.PermittedActions.Should().ContainSingle().Which.Should().Be("ReviewLegacy");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolved_or_absent_unidentified_history_keeps_ready_action_available(bool withPaidHistory)
    {
        if (withPaidHistory) await AddUnidentifiedOrderAsync(OrderStatus.Completed, paid: true);
        var table = await ReadTableAsync();
        table.HasLegacyAmbiguity.Should().BeFalse();
        table.State.Should().Be("NeedsReset");
        table.PermittedActions.Should().ContainSingle().Which.Should().Be("MarkTableReady");
    }

    [Fact]
    public async Task Identified_partial_refund_debt_routes_to_review_without_offering_collection_or_ready()
    {
        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = new Order
            {
                OrderNumber = "READY-REFUND",
                Type = OrderType.DineIn,
                Status = OrderStatus.Completed,
                TableId = _tableId,
                Total = 10m,
                TotalPaid = 1m,
                RemainingAmount = 9m,
                PaymentStatus = PaymentStatus.PartiallyPaid,
                OrderDate = DateTime.UtcNow,
                CreatedBy = nameof(ServerFloorReadinessAmbiguityTests)
            };
            order.Payments.Add(new OrderPayment
            {
                Amount = 2m,
                RefundedAmount = 1m,
                Currency = "CHF",
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentStatus.PartiallyRefunded,
                PaymentDate = DateTime.UtcNow,
                TransactionId = "ready-partial-refund",
                CreatedBy = nameof(ServerFloorReadinessAmbiguityTests)
            });
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }
        var table = await ReadTableAsync();
        table.HasLegacyAmbiguity.Should().BeTrue("CHF 9 unresolved debt remains after the refund");
        table.State.Should().Be("Ambiguous");
        table.PermittedActions.Should().ContainSingle().Which.Should().Be("ReviewLegacy");
        table.Legacy.Should().BeNull("reversed tender is held for review rather than offered for collection");
    }

    [Fact]
    public async Task Floor_ready_action_posts_the_guarded_transition_and_allows_owner_readback()
    {
        AuthenticateAsRole(UserRole.Server);
        var operationId = Guid.NewGuid();
        using var response = await PostAsJsonAsync($"/api/Tables/{_tableId}/ready", new
        {
            operationId,
            expectedReadinessVersion = 1
        });
        response.EnsureSuccessStatusCode();
        var outcome = await GetFromJsonAsync<ApiResponse<TableReadinessOperationDto>>(
            $"/api/Tables/{_tableId}/ready/operations/{operationId}");
        outcome!.Success.Should().BeTrue();
        outcome.Data!.ReadinessState.Should().Be("ReadyForGuests");
        outcome.Data.ReadinessVersion.Should().Be(2);
        var table = await ReadTableAsync();
        table.State.Should().Be("Available");
        table.PermittedActions.Should().ContainSingle().Which.Should().Be("StartTable");
    }

    private async Task AddUnidentifiedOrderAsync(OrderStatus status, bool paid)
    {
        await using var context = DatabaseFixture.CreateContext();
        var order = new Order
        {
            OrderNumber = "READY-LEGACY",
            Type = OrderType.DineIn,
            Status = status,
            TableId = null,
            TableNumber = null,
            ServiceSessionId = null,
            OrderDate = DateTime.UtcNow,
            Total = 10m,
            TotalPaid = paid ? 10m : 0m,
            RemainingAmount = paid ? 99m : 0m,
            PaymentStatus = paid ? PaymentStatus.Completed : PaymentStatus.Pending,
            CreatedBy = nameof(ServerFloorReadinessAmbiguityTests)
        };
        if (paid)
            order.Payments.Add(new OrderPayment
            {
                Amount = 10m,
                Currency = "CHF",
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentStatus.Completed,
                PaymentDate = DateTime.UtcNow,
                TransactionId = "ready-native-control",
                CreatedBy = nameof(ServerFloorReadinessAmbiguityTests)
            });
        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }

    private async Task<ServerFloorTableDto> ReadTableAsync()
    {
        AuthenticateAsRole(UserRole.Server);
        var body = await GetFromJsonAsync<ApiResponse<ServerFloorSnapshotDto>>("/api/staff/server-workspace/floor");
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue(body.Message);
        return body.Data!.Tables.Single(table => table.TableId == _tableId);
    }
}
