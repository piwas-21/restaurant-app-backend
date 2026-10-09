using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class GuestRoundKitchenRoutingTests : IntegrationTestBase
{
    private readonly SnapshotFailureSwitch _snapshotFailure = new();
    private Guid _tableId;
    private Guid _productId;
    private string _qrCode = string.Empty;

    public GuestRoundKitchenRoutingTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantModules>();
        services.AddSingleton<ITenantModules>(new GuestRoundModules());
        services.RemoveAll<ITenantFeatures>();
        services.AddSingleton<ITenantFeatures>(new GuestRoundFeatures());
        services.AddSingleton(_snapshotFailure);
        services.RemoveAll<IOrderNativeBillingAcceptance>();
        services.AddScoped<IOrderNativeBillingAcceptance>(provider => new SnapshotFaultingBillingAcceptance(
            provider.GetRequiredService<IOrderFidelityCoordinator>(),
            provider.GetRequiredService<IOrderBillingSnapshotWriter>(),
            provider.GetRequiredService<IOrderBillingAwardSuppressionWriter>(),
            provider.GetRequiredService<SnapshotFailureSwitch>()));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products.Where(value => value.Name == "Test Pizza")
            .Select(value => value.Id).SingleAsync();
        _tableId = Guid.NewGuid();
        _qrCode = $"guest-routing-{Guid.NewGuid():N}";
        context.Tables.Add(new Table
        {
            Id = _tableId,
            TableNumber = "17",
            MaxGuests = 4,
            IsActive = true,
            QRCodeData = _qrCode,
            QRCodeGeneratedAt = DateTime.UtcNow,
            CreatedBy = nameof(GuestRoundKitchenRoutingTests),
        });
        context.OrderTypeConfigurations.Add(new OrderTypeConfiguration
        {
            OrderType = OrderType.DineIn,
            IsEnabled = true,
            DisplayOrder = (int)OrderType.DineIn,
            EnforceOpeningHours = false,
            CreatedBy = nameof(GuestRoundKitchenRoutingTests),
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Guest_round_routes_rollback_replay_once_and_complete_on_no_printer_board()
    {
        await AssertNoPrinterSingleKitchenAsync();

        AuthenticateAsRole(UserRole.Server);
        var sessionResponse = await PostAsJsonAsync("/api/table-service-sessions", new { tableId = _tableId });
        var session = (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(sessionResponse))!.Data!;
        var admissionResponse = await PostAsJsonAsync(
            $"/api/table-guest-visits/{session.ServiceSessionId}/admission-code", new { });
        var admission = (await ReadResponseAsync<ApiResponse<TableGuestAdmissionCodeDto>>(admissionResponse))!.Data!;

        AuthenticateAsAnonymous();
        var joinResponse = await PostAsJsonAsync("/api/table-guest-visits/join", new
        {
            qrCodeData = _qrCode,
            admissionCode = admission.AdmissionCode,
        });
        var participant = (await ReadResponseAsync<ApiResponse<TableGuestJoinDto>>(joinResponse))!.Data!;
        var basketSession = Guid.NewGuid().ToString("N");
        Client.DefaultRequestHeaders.Add("X-Session-Id", basketSession);
        Client.DefaultRequestHeaders.Add("X-Table-Participant", participant.ParticipantToken);

        (await Client.PutAsJsonAsync("/api/Basket/order-type", new { orderType = "DineIn" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsJsonAsync("/api/Basket/items", new AddToBasketDto
        {
            ProductId = _productId,
            Quantity = 1,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = (await ReadResponseAsync<ApiResponse<BasketDto>>(
            await Client.GetAsync("/api/Basket")))!.Data!;
        var account = (await ReadResponseAsync<ApiResponse<TableGuestAccountDto>>(
            await Client.GetAsync($"/api/table-guest-visits/{session.ServiceSessionId}/account")))!.Data!;
        account.AccountRevision.Should().Be(1);

        var operationId = Guid.NewGuid();
        var request = new
        {
            operationId,
            expectedAccountRevision = account.AccountRevision,
            expectedBasketFingerprint = basket.PurchaseFingerprint,
        };
        _snapshotFailure.FailNextSnapshot = 1;
        var failed = await Client.PostAsJsonAsync(
            $"/api/table-guest-visits/{session.ServiceSessionId}/rounds", request);
        failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await AssertRoundTransactionRolledBackAsync(session.ServiceSessionId);

        var createdResponse = await Client.PostAsJsonAsync(
            $"/api/table-guest-visits/{session.ServiceSessionId}/rounds", request);
        createdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await ReadResponseAsync<ApiResponse<TableGuestAccountDto>>(createdResponse))!;
        created.Success.Should().BeTrue();
        created.Data!.AccountRevision.Should().Be(2);
        var orderId = created.Data.Orders.Should().ContainSingle().Which.OrderId;
        var committedRoutes = await ReadRouteIdentityAsync(orderId);
        committedRoutes.Should().NotBeEmpty();

        var replayResponse = await Client.PostAsJsonAsync(
            $"/api/table-guest-visits/{session.ServiceSessionId}/rounds", request);
        var replay = (await ReadResponseAsync<ApiResponse<TableGuestAccountDto>>(replayResponse))!;
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.Success.Should().BeTrue();
        replay.Data!.Orders.Should().ContainSingle().Which.OrderId.Should().Be(orderId);

        await using (var verify = DatabaseFixture.CreateContext())
        {
            (await verify.Orders.CountAsync(value => value.ServiceSessionId == session.ServiceSessionId))
                .Should().Be(1);
            (await verify.TableGuestRoundOperations.CountAsync(value => value.ServiceSessionId == session.ServiceSessionId))
                .Should().Be(1);
            (await verify.TableServiceSessions.Where(value => value.Id == session.ServiceSessionId)
                .Select(value => value.AccountRevision).SingleAsync()).Should().Be(2);
            var routes = await verify.OrderRoutingStates.Where(value => value.OrderId == orderId).ToListAsync();
            routes.Select(Identity).OrderBy(value => value.Target).Should().Equal(committedRoutes);
            routes.Should().HaveCount(2);
            routes.Should().ContainSingle(value => value.IsRequired && value.Target == DevicePrintTarget.General
                && value.Status == DevicePrintStatus.NotConfigured && value.DeviceId == null);
            routes.Should().ContainSingle(value => !value.IsRequired && value.Target == DevicePrintTarget.Cashier
                && value.Status == DevicePrintStatus.NotConfigured && value.DeviceId == null);
            routes.Should().NotContain(value => value.Status == DevicePrintStatus.Printed);
        }

        AuthenticateAsRole(UserRole.Server);
        var currentOrder = await ReadOrderAsync(orderId);
        AuthenticateAsRole(UserRole.KitchenStaff);
        currentOrder = await UpdateOrderStatusAsync(orderId, OrderStatus.Preparing, currentOrder.Version);
        currentOrder = await UpdateOrderStatusAsync(orderId, OrderStatus.Ready, currentOrder.Version);
        var feed = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(
            await Client.GetAsync("/api/staff/kitchen-board/work")))!.Data!;
        var boardOrder = feed.Orders.Items.Should().ContainSingle(value => value.OrderId == orderId).Which;
        boardOrder.CanComplete.Should().BeTrue();
        boardOrder.RequiredKitchenRoutes.Should().ContainSingle(value =>
            value.Target == nameof(DevicePrintTarget.General)
            && value.Status == nameof(DevicePrintStatus.NotConfigured));
        var completionResponse = await Client.PostAsJsonAsync(
            $"/api/staff/kitchen-board/orders/{orderId}/work-items/{orderId}/complete",
            new { kind = "InitialOrder", expectedOrderVersion = currentOrder.Version, expectedAccountRevision = (long?)null });
        var completion = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkCompletionDto>>(completionResponse))!;
        completionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        completion.Data!.IsCompleted.Should().BeTrue();
        completion.Data.WorkItemId.Should().Be(orderId);

        await using var final = DatabaseFixture.CreateContext();
        (await final.KitchenBoardWorkCompletions.CountAsync(value => value.OrderId == orderId))
            .Should().Be(1);
        (await final.DeviceOrderReceipts.CountAsync(value => value.OrderId == orderId)).Should().Be(0);
        (await final.OrderRoutingStates.Where(value => value.OrderId == orderId)
            .Select(value => value.Status).ToListAsync()).Should().NotContain(DevicePrintStatus.Printed);
    }

    private async Task AssertRoundTransactionRolledBackAsync(Guid sessionId)
    {
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync(value => value.ServiceSessionId == sessionId)).Should().Be(0);
        (await context.OrderRoutingStates.CountAsync()).Should().Be(0);
        (await context.TableGuestRoundOperations.CountAsync()).Should().Be(0);
        (await context.OrderBillingSnapshots.CountAsync()).Should().Be(0);
        (await context.TableServiceSessions.Where(value => value.Id == sessionId)
            .Select(value => value.AccountRevision).SingleAsync()).Should().Be(1);
    }

    private async Task AssertNoPrinterSingleKitchenAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.PrinterDevices.AnyAsync()).Should().BeFalse();
        (await context.PrinterDeviceTargetCapabilities.AnyAsync()).Should().BeFalse();

        var readinessProvider = scope.ServiceProvider.GetRequiredService<IOrderRoutingReadinessSnapshotProvider>();
        var readiness = await readinessProvider.LoadAsync(CancellationToken.None);
        readiness.RoutingMode.Should().Be(DeviceKitchenRoutingMode.SingleKitchen);
        readiness.SelectDevice(DevicePrintTarget.General).Should().BeNull();
    }

    private async Task<(Guid Id, Guid JobId, DevicePrintTarget Target, int Revision, int Version)[]>
        ReadRouteIdentityAsync(Guid orderId)
    {
        await using var context = DatabaseFixture.CreateContext();
        var routes = await context.OrderRoutingStates.AsNoTracking().Where(value => value.OrderId == orderId)
            .OrderBy(value => value.Target).Select(value => new
            {
                value.Id,
                value.JobId,
                value.Target,
                value.Revision,
                value.Version,
            })
            .ToListAsync();
        return routes
            .Select(value => (value.Id, value.JobId, value.Target, value.Revision, value.Version))
            .ToArray();
    }

    private static (Guid Id, Guid JobId, DevicePrintTarget Target, int Revision, int Version) Identity(
        OrderRoutingState value) => (value.Id, value.JobId, value.Target, value.Revision, value.Version);

    private async Task<OrderDto> ReadOrderAsync(Guid orderId)
    {
        var response = await Client.GetAsync($"/api/Orders/{orderId}");
        return (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;
    }

    private async Task<OrderDto> UpdateOrderStatusAsync(Guid orderId, OrderStatus status, int version)
    {
        var response = await Client.PutAsJsonAsync($"/api/Orders/{orderId}/status", new
        {
            newStatus = status.ToString(),
            expectedVersion = version,
        });
        var result = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
        result.Success.Should().BeTrue($"status update to {status} failed: {string.Join("; ", result.Errors ?? [])}");
        return result.Data!;
    }

    private sealed class SnapshotFailureSwitch
    {
        public int FailNextSnapshot;
    }

    private sealed class SnapshotFaultingBillingAcceptance(
        IOrderFidelityCoordinator fidelity,
        IOrderBillingSnapshotWriter snapshots,
        IOrderBillingAwardSuppressionWriter suppressions,
        SnapshotFailureSwitch failure) : IOrderNativeBillingAcceptance
    {
        public Task<OrderBillingEarningEvaluation?> CalculatePointsToEarnAsync(
            Order order, decimal itemsTotal, Guid? userId, CancellationToken cancellationToken) =>
            fidelity.CalculatePointsToEarnAsync(order, itemsTotal, userId, cancellationToken);

        public Task PreviewRedemptionAsync(
            Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken) =>
            fidelity.PreviewRedemptionAsync(order, pointsToRedeem, userId, cancellationToken);

        public Task<OrderBillingRedemptionEvidence?> RedeemAsync(
            Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken,
            bool failOnError = false) =>
            fidelity.RedeemAsync(order, pointsToRedeem, userId, cancellationToken, failOnError);

        public Task AwardEarnedPointsAsync(Order order, CancellationToken cancellationToken) =>
            fidelity.AwardEarnedPointsAsync(order, cancellationToken);

        public Task RecordRemovedEarningUnitsAsync(Guid orderId, Guid amendmentId, CancellationToken cancellationToken) =>
            suppressions.RecordRemovedUnitsAsync(orderId, amendmentId, cancellationToken);

        public async Task WriteAcceptedSnapshotAsync(
            Order order, string? acceptedCurrency, OrderBillingEarningEvaluation? earning,
            OrderBillingRedemptionEvidence? redemption, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref failure.FailNextSnapshot, 0) == 1)
                throw new InvalidOperationException("Injected accepted-snapshot failure.");
            await snapshots.WriteAsync(order, acceptedCurrency, earning, redemption, cancellationToken);
        }
    }

    private sealed class GuestRoundModules : ITenantModules
    {
        private static readonly string[] Enabled =
            [ModuleIds.Core, ModuleIds.KitchenBoard, ModuleIds.Server, ModuleIds.Cashier, ModuleIds.Printing];

        public bool IsEnforced => true;
        public IReadOnlyList<string> EnabledModules => Enabled;
        public bool IsEnabled(string moduleId) => Enabled.Contains(moduleId, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class GuestRoundFeatures : ITenantFeatures
    {
        public bool ServerWorkspaceV2 => true;
        public bool TableAccountV1 => true;
        public bool OrderAmendmentsV1 => true;
        public bool TableGuestVisitsV1 => true;
        public bool TableVisitReadinessV1 => false;
        public bool TableAccountPaymentsV1 => false;
        public bool ServerAccountCollectionV1 => false;
        public bool TableGuestAccountPaymentsV1 => false;
        public bool EnforceSauceMinimum => false;
        public bool OptionSetMaterializationEnabled => false;
    }
}
