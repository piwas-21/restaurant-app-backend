using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.KitchenBoard.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.KitchenBoard;

[Collection("Database Lane 3")]
public sealed class KitchenBoardWorkflowTests : IntegrationTestBase
{
    private static readonly DateTime TestNow = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private readonly KitchenBoardTestFeatures _features = new();

    public KitchenBoardWorkflowTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantModules>();
        services.AddSingleton<ITenantModules>(new KitchenBoardModules());
        services.RemoveAll<ITenantFeatures>();
        services.AddSingleton<ITenantFeatures>(_features);
    }

    [Fact]
    public async Task Kitchen_board_is_hidden_and_legacy_status_write_is_unchanged_when_flags_are_off()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Ready, DevicePrintStatus.NotConfigured);
        _features.Enabled = false;

        AuthenticateAsRole(UserRole.KitchenStaff);
        (await Client.GetAsync("/api/staff/kitchen-board/work")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await Client.PostAsync(
            $"/api/staff/kitchen-board/orders/{orderId}/work-items/{orderId}/complete",
            System.Net.Http.Json.JsonContent.Create(new
            {
                kind = "InitialOrder",
                expectedOrderVersion = 1,
            }))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        AuthenticateAsRole(UserRole.Server);
        var legacy = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/Orders/{orderId}/status", new
            {
                newStatus = nameof(OrderStatus.Completed),
                expectedVersion = 1,
            })))!;
        legacy.Success.Should().BeTrue("the new guard must be inert with both rollout flags off");
    }

    [Fact]
    public async Task Direct_status_handover_requires_base_work_and_every_active_correction()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Ready, DevicePrintStatus.NotConfigured);
        AuthenticateAsRole(UserRole.Server);

        var noBaseProof = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/Orders/{orderId}/status", new
            {
                newStatus = nameof(OrderStatus.Completed),
                expectedVersion = 1,
            })))!;
        noBaseProof.Success.Should().BeFalse();
        noBaseProof.ErrorCode.Should().Be(ErrorCodes.KitchenWorkUnresolved);

        AuthenticateAsRole(UserRole.KitchenStaff);
        var versionBeforeInitialAck = await GetOrderVersionAsync(orderId);
        (await CompleteAsync(orderId, orderId, "InitialOrder", versionBeforeInitialAck, null)).Success.Should().BeTrue();
        var correctionId = await SeedActiveCorrectionAsync(orderId);

        AuthenticateAsRole(UserRole.Server);
        var versionBeforeCorrectionAck = await GetOrderVersionAsync(orderId);
        var noCorrectionAck = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/Orders/{orderId}/status", new
            {
                newStatus = nameof(OrderStatus.Completed),
                expectedVersion = versionBeforeCorrectionAck,
            })))!;
        noCorrectionAck.Success.Should().BeFalse();
        noCorrectionAck.ErrorCode.Should().Be(ErrorCodes.KitchenCorrectionUnresolved);

        AuthenticateAsRole(UserRole.KitchenStaff);
        (await CompleteAsync(orderId, correctionId, "AmendmentCorrection", versionBeforeCorrectionAck, 1))
            .Success.Should().BeTrue();
        AuthenticateAsRole(UserRole.Server);
        var versionAfterCorrectionAck = await GetOrderVersionAsync(orderId);
        var completed = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/Orders/{orderId}/status", new
            {
                newStatus = nameof(OrderStatus.Completed),
                expectedVersion = versionAfterCorrectionAck,
            })))!;
        completed.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Required_cashier_exception_is_not_discharged_by_kitchen_board_proof()
    {
        var orderId = await SeedOrderWithCashierExceptionAsync();
        AuthenticateAsRole(UserRole.KitchenStaff);
        (await CompleteAsync(orderId, orderId, "InitialOrder", await GetOrderVersionAsync(orderId), null))
            .Success.Should().BeTrue();

        AuthenticateAsRole(UserRole.Server);
        var handover = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/Orders/{orderId}/status", new
            {
                newStatus = nameof(OrderStatus.Completed),
                expectedVersion = await GetOrderVersionAsync(orderId),
            })))!;
        handover.Success.Should().BeFalse();
        handover.ErrorCode.Should().Be(ErrorCodes.RequiredRoutingUnresolved);

        var task = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=exception")))!.Data!;
        task.Items.Single(item => item.OrderId == orderId)
            .PermittedDeliveryActions.Single().Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Explicit_order_receipt_for_the_current_route_allows_printer_handover()
    {
        var (orderId, route, deviceId) = await SeedConfiguredOrderAsync(DevicePrintStatus.Sent);
        AuthenticateAsDevice();
        using var acknowledgement = new HttpRequestMessage(HttpMethod.Post, "/api/devices/print-acks")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                acks = new[]
                {
                    new
                    {
                        orderId,
                        target = route.Target.ToString(),
                        status = nameof(DevicePrintStatus.Printed),
                        receivedAt = TestNow,
                        printedAt = TestNow,
                        copies = 1,
                        jobId = route.JobId,
                        revision = route.Revision,
                        jobType = nameof(DevicePrintJobType.Order),
                    }
                }
            })
        };
        acknowledgement.Headers.Add("X-Device-Id", deviceId);
        var acknowledgementResponse = await Client.SendAsync(acknowledgement);
        acknowledgementResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await acknowledgementResponse.Content.ReadAsStringAsync());

        await using (var context = DatabaseFixture.CreateContext())
        {
            var receipt = await context.DeviceOrderReceipts.SingleAsync(value => value.OrderId == orderId);
            receipt.JobId.Should().Be(route.JobId);
            receipt.Revision.Should().Be(route.Revision);
            receipt.JobType.Should().Be(DevicePrintJobType.Order);
            receipt.DeviceId.Should().Be(deviceId);
            receipt.Target.Should().Be(route.Target);
            receipt.Status.Should().Be(DevicePrintStatus.Printed);
        }

        AuthenticateAsRole(UserRole.Server);
        var handover = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/Orders/{orderId}/status", new
            {
                newStatus = nameof(OrderStatus.Completed),
                expectedVersion = await GetOrderVersionAsync(orderId),
            })))!;
        handover.Success.Should().BeTrue(
            $"the durable original-order receipt matches the live route exactly: {string.Join(";", handover.Errors ?? [])} ({handover.ErrorCode})");
    }

    [Fact]
    public async Task Legacy_order_receipt_for_the_current_route_remains_compatible()
    {
        var (orderId, route, deviceId) = await SeedConfiguredOrderAsync(DevicePrintStatus.Sent);
        AuthenticateAsDevice();
        using var acknowledgement = new HttpRequestMessage(HttpMethod.Post, "/api/devices/print-acks")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                acks = new[]
                {
                    new
                    {
                        orderId,
                        target = route.Target.ToString(),
                        status = nameof(DevicePrintStatus.Printed),
                        receivedAt = TestNow,
                        printedAt = TestNow,
                        copies = 1,
                    }
                }
            })
        };
        acknowledgement.Headers.Add("X-Device-Id", deviceId);
        var response = await Client.SendAsync(acknowledgement);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        await using (var context = DatabaseFixture.CreateContext())
        {
            var receipt = await context.DeviceOrderReceipts.SingleAsync(value => value.OrderId == orderId);
            receipt.JobId.Should().BeNull();
            receipt.Revision.Should().BeNull();
            receipt.JobType.Should().BeNull();
        }

        AuthenticateAsRole(UserRole.Server);
        var handover = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/Orders/{orderId}/status", new
            {
                newStatus = nameof(OrderStatus.Completed),
                expectedVersion = await GetOrderVersionAsync(orderId),
            })))!;
        handover.Success.Should().BeTrue(
            $"the legacy acknowledgement is accepted only for its configured order route: {handover.ErrorCode}");
    }

    [Theory]
    [InlineData("wrong-revision")]
    [InlineData("update-job")]
    [InlineData("cashier-copy")]
    public async Task Nonmatching_printer_receipt_does_not_prove_original_kitchen_work(string receiptKind)
    {
        var (orderId, route, deviceId) = await SeedConfiguredOrderAsync(DevicePrintStatus.Printed);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.DeviceOrderReceipts.Add(new DeviceOrderReceipt
            {
                OrderId = orderId,
                DeviceId = deviceId,
                Target = receiptKind == "cashier-copy" ? DevicePrintTarget.Cashier : route.Target,
                JobId = route.JobId,
                Revision = receiptKind == "wrong-revision" ? route.Revision - 1 : route.Revision,
                JobType = receiptKind == "update-job" ? DevicePrintJobType.Update : DevicePrintJobType.Order,
                Status = DevicePrintStatus.Printed,
                ReceivedAt = TestNow,
                PrintedAt = TestNow,
                Copies = 1,
                CreatedBy = nameof(KitchenBoardWorkflowTests),
            });
            await context.SaveChangesAsync();
        }

        AuthenticateAsRole(UserRole.Server);
        var handover = (await ReadResponseAsync<ApiResponse<OrderDto>>(await Client.PutAsJsonAsync(
            $"/api/Orders/{orderId}/status", new
            {
                newStatus = nameof(OrderStatus.Completed),
                expectedVersion = await GetOrderVersionAsync(orderId),
            })))!;
        handover.Success.Should().BeFalse(receiptKind);
        handover.ErrorCode.Should().Be(ErrorCodes.RequiredRoutingUnresolved);
    }

    [Fact]
    public async Task Missing_required_routes_cannot_be_completed_as_a_no_printer_order()
    {
        var orderId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Orders.Add(NewOrder(orderId, OrderStatus.Ready));
            await context.SaveChangesAsync();
        }

        AuthenticateAsRole(UserRole.KitchenStaff);
        var feed = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(
            await Client.GetAsync("/api/staff/kitchen-board/work")))!.Data!;
        feed.Orders.Items.Single(item => item.OrderId == orderId).CanComplete.Should().BeFalse();
        var response = await Client.PostAsync(
            $"/api/staff/kitchen-board/orders/{orderId}/work-items/{orderId}/complete",
            System.Net.Http.Json.JsonContent.Create(new
            {
                kind = "InitialOrder",
                expectedOrderVersion = 1,
            }));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadResponseAsync<ApiResponse<KitchenBoardWorkCompletionDto>>(response))!
            .ErrorCode.Should().Be(ErrorCodes.RequiredRoutingUnresolved);
    }

    [Fact]
    public async Task Unconfigured_ready_order_requires_board_proof_before_server_delivery()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Ready, DevicePrintStatus.NotConfigured);

        AuthenticateAsRole(UserRole.Server);
        (await Client.GetAsync("/api/staff/kitchen-board/work")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        AuthenticateAsRole(UserRole.KitchenStaff);
        var response = await Client.GetAsync("/api/staff/kitchen-board/work?pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(response))!;
        var order = body.Data!.Orders.Items.Single(item => item.OrderId == orderId);
        order.Status.Should().Be(nameof(OrderStatus.Ready));
        order.CanComplete.Should().BeTrue();
        order.RequiredKitchenRoutes.Should().ContainSingle(route =>
            route.Target == nameof(DevicePrintTarget.General)
            && route.Status == nameof(DevicePrintStatus.NotConfigured));
        var json = await response.Content.ReadAsStringAsync();
        json.ToLowerInvariant().Should().NotContain("customeremail");
        json.ToLowerInvariant().Should().NotContain("unitprice");
        json.ToLowerInvariant().Should().NotContain("remainingamount");

        AuthenticateAsRole(UserRole.Server);
        var blocked = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=exception")))!.Data!;
        var blockedAction = blocked.Items.Single(item => item.OrderId == orderId)
            .PermittedDeliveryActions.Single();
        blockedAction.Allowed.Should().BeFalse();
        blockedAction.ReasonCode.Should().Be(ErrorCodes.KitchenWorkUnresolved);

        AuthenticateAsRole(UserRole.KitchenStaff);
        var completion = await CompleteAsync(orderId, orderId, "InitialOrder",
            await GetOrderVersionAsync(orderId), null);
        completion.Success.Should().BeTrue();
        completion.Data!.AcknowledgedOrderVersion.Should().Be(1);

        AuthenticateAsRole(UserRole.Server);
        var enabled = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=exception")))!.Data!;
        enabled.Items.Single(item => item.OrderId == orderId)
            .PermittedDeliveryActions.Single().Allowed.Should().BeTrue();

        var delivered = (await ReadResponseAsync<ApiResponse<OrderDto>>(await PostAsJsonAsync(
            $"/api/staff/server-workspace/tasks/{orderId}/deliver",
            new { expectedVersion = await GetOrderVersionAsync(orderId) })))!;
        delivered.Success.Should().BeTrue();
        delivered.Data!.Status.Should().Be(nameof(OrderStatus.Completed));

        AuthenticateAsRole(UserRole.KitchenStaff);
        var replay = await CompleteAsync(orderId, orderId, "InitialOrder", 1, null);
        replay.Success.Should().BeTrue("the durable completion is an idempotent replay after handover");
    }

    [Theory]
    [InlineData(DevicePrintStatus.Failed)]
    [InlineData(DevicePrintStatus.Unknown)]
    [InlineData(DevicePrintStatus.Skipped)]
    public async Task Failed_or_unknown_printer_state_is_not_reclassified_as_no_printer(
        DevicePrintStatus routeStatus)
    {
        var orderId = await SeedOrderAsync(OrderStatus.Ready, routeStatus);
        AuthenticateAsRole(UserRole.KitchenStaff);

        var feed = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(
            await Client.GetAsync("/api/staff/kitchen-board/work")))!.Data!;
        feed.Orders.Items.Single(item => item.OrderId == orderId).CanComplete.Should().BeFalse();

        var refusal = await Client.PostAsync(
            $"/api/staff/kitchen-board/orders/{orderId}/work-items/{orderId}/complete",
            System.Net.Http.Json.JsonContent.Create(new
            {
                kind = "InitialOrder",
                expectedOrderVersion = 1,
            }));
        refusal.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadResponseAsync<ApiResponse<KitchenBoardWorkCompletionDto>>(refusal))!
            .ErrorCode.Should().Be(ErrorCodes.RequiredRoutingUnresolved);
    }

    [Fact]
    public async Task Terminal_full_void_correction_has_order_version_and_immutable_revision()
    {
        var (orderId, workItemId, sessionId) = await SeedTerminalCorrectionAsync();
        AuthenticateAsRole(UserRole.KitchenStaff);

        var initialResponse = await Client.GetAsync("/api/staff/kitchen-board/work?pageSize=10");
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(initialResponse))!.Data!;
        var correction = initial.Corrections.Items.Single(item => item.WorkItemId == workItemId);
        correction.Status.Should().Be(nameof(OrderStatus.Cancelled));
        correction.OrderVersion.Should().Be(6);
        correction.AccountRevision.Should().Be(14);
        correction.CanComplete.Should().BeTrue();
        correction.IsCompleted.Should().BeFalse();
        var initialCursor = initial.Corrections.NextCursor;
        initialCursor.Should().NotBeNullOrWhiteSpace();

        var stale = await CompleteAsync(orderId, workItemId, "AmendmentCorrection", 5, 14);
        stale.Success.Should().BeFalse();
        stale.ErrorCode.Should().Be(ErrorCodes.OrderVersionConflict);

        var acknowledged = await CompleteAsync(orderId, workItemId, "AmendmentCorrection", 6, 14);
        acknowledged.Success.Should().BeTrue(string.Join("; ", acknowledged.Errors ?? []));
        acknowledged.Data!.AccountRevision.Should().Be(14);
        acknowledged.Data.AcknowledgedOrderVersion.Should().Be(6);

        await using (var context = DatabaseFixture.CreateContext())
        {
            await context.Orders.Where(order => order.Id == orderId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.Version, 7));
        }

        var replay = await CompleteAsync(orderId, workItemId, "AmendmentCorrection", 6, 14);
        replay.Success.Should().BeTrue("the correction identity is its note revision, not today's order version");

        var next = await Client.GetAsync(
            $"/api/staff/kitchen-board/work?pageSize=10&correctionsCursor={Uri.EscapeDataString(initialCursor!)}");
        var changed = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(next))!.Data!;
        changed.Corrections.Items.Should().BeEmpty("acknowledged corrections leave the active-work feed");
        changed.Corrections.RemovedIds.Should().Contain(workItemId);

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.KitchenBoardWorkCompletions.CountAsync(work => work.OrderId == orderId
            && work.WorkItemId == workItemId
            && work.Kind == KitchenBoardWorkKind.AmendmentCorrection)).Should().Be(1);
        (await verify.TableServiceSessions.SingleAsync(session => session.Id == sessionId))
            .AccountRevision.Should().Be(15, "the correction retains its committed revision");
    }

    [Fact]
    public async Task Withdrawn_correction_is_a_redacted_tombstone_and_does_not_block_close_guard()
    {
        var (orderId, workItemId, sessionId) = await SeedTerminalCorrectionAsync();
        AuthenticateAsRole(UserRole.KitchenStaff);
        var initial = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(
            await Client.GetAsync("/api/staff/kitchen-board/work?pageSize=10")))!.Data!;
        var cursor = initial.Corrections.NextCursor!;
        initial.Corrections.Items.Single(item => item.WorkItemId == workItemId).Withdrawn.Should().BeFalse();

        await using (var context = DatabaseFixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var note = await context.OrderOperationalNotes.SingleAsync(value => value.Id == workItemId);
            note.Text = "[erased]";
            note.KitchenChangesJson = null;
            note.AmendmentId = null;
            note.AccountRevision = null;
            note.KitchenTarget = null;
            note.WithdrawnAt = TestNow;
            note.UpdatedAt = TestNow;
            note.UpdatedBy = nameof(KitchenBoardWorkflowTests);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var deltaResponse = await Client.GetAsync(
            $"/api/staff/kitchen-board/work?pageSize=10&correctionsCursor={Uri.EscapeDataString(cursor)}");
        var delta = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(deltaResponse))!.Data!;
        var tombstone = delta.Corrections.Items.Should().ContainSingle(item => item.WorkItemId == workItemId).Subject;
        tombstone.Withdrawn.Should().BeTrue();
        tombstone.IsCompleted.Should().BeTrue();
        tombstone.Summary.Should().BeEmpty();
        tombstone.Changes.Should().BeEmpty();

        await using var verify = DatabaseFixture.CreateContext();
        (await KitchenBoardCloseGuard.HasUnresolvedCorrectionAsync(
            verify, sessionId, null, null, CancellationToken.None)).Should().BeFalse();
        var retained = await verify.OrderOperationalNotes.IgnoreQueryFilters()
            .SingleAsync(value => value.Id == workItemId);
        retained.KitchenChangesJson.Should().BeNull("a withdrawn tombstone must not need the erased payload");
        retained.AmendmentId.Should().BeNull();
        retained.AccountRevision.Should().BeNull();
        retained.KitchenTarget.Should().BeNull();
    }

    [Fact]
    public async Task Printer_receipt_completion_advances_correction_cursor()
    {
        var (orderId, workItemId, _) = await SeedTerminalCorrectionAsync();
        var deviceId = $"kitchen-board-{Guid.NewGuid():N}";
        await ConfigureCorrectionRouteAsync(orderId, deviceId);
        AuthenticateAsRole(UserRole.KitchenStaff);
        var initial = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(
            await Client.GetAsync("/api/staff/kitchen-board/work?pageSize=10")))!.Data!;
        var cursor = initial.Corrections.NextCursor!;
        initial.Corrections.Items.Single(item => item.WorkItemId == workItemId)
            .IsCompleted.Should().BeFalse();

        async Task SendAckAsync(string status)
        {
            AuthenticateAsDevice();
            using var acknowledgement = new HttpRequestMessage(HttpMethod.Post, "/api/devices/print-acks")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new
                {
                    acks = new[]
                    {
                        new
                        {
                            orderId,
                            target = "General",
                            status,
                            receivedAt = TestNow,
                            printedAt = status == nameof(DevicePrintStatus.Printed) ? TestNow : (DateTime?)null,
                            failureReason = status == nameof(DevicePrintStatus.Failed) ? "Offline" : null,
                            copies = 1,
                            jobId = workItemId,
                            revision = PrinterUpdateRevisions.Original,
                            jobType = DevicePrintJobType.Update.ToString(),
                        }
                    }
                })
            };
            acknowledgement.Headers.Add("X-Device-Id", deviceId);
            var response = await Client.SendAsync(acknowledgement);
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
        }

        async Task<KitchenBoardPageDto<KitchenBoardCorrectionDto>> ReadDeltaAsync(string value)
        {
            AuthenticateAsRole(UserRole.KitchenStaff);
            var response = await Client.GetAsync(
                $"/api/staff/kitchen-board/work?pageSize=10&correctionsCursor={Uri.EscapeDataString(value)}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return ((await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(response))!).Data!.Corrections;
        }

        await SendAckAsync(nameof(DevicePrintStatus.Printed));
        var printed = await ReadDeltaAsync(cursor);
        printed.Items.Should().BeEmpty("completed corrections leave the active-work feed");
        printed.RemovedIds.Should().Contain(workItemId);

        await SendAckAsync(nameof(DevicePrintStatus.Failed));
        var failed = await ReadDeltaAsync(printed.NextCursor!);
        failed.Items.Should().ContainSingle(item => item.WorkItemId == workItemId && !item.IsCompleted);

        await SendAckAsync(nameof(DevicePrintStatus.Printed));
        var reprinted = await ReadDeltaAsync(failed.NextCursor!);
        reprinted.Items.Should().BeEmpty();
        reprinted.RemovedIds.Should().Contain(workItemId);
    }

    [Theory]
    [InlineData("wrong-order")]
    [InlineData("original-order-job")]
    [InlineData("wrong-device")]
    public async Task Only_matching_update_receipt_for_the_configured_route_completes_a_correction(
        string receiptKind)
    {
        var (orderId, workItemId, _) = await SeedTerminalCorrectionAsync();
        var deviceId = $"kitchen-board-{Guid.NewGuid():N}";
        await ConfigureCorrectionRouteAsync(orderId, deviceId);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.DeviceOrderReceipts.Add(new DeviceOrderReceipt
            {
                OrderId = receiptKind == "wrong-order" ? Guid.NewGuid() : orderId,
                DeviceId = receiptKind == "wrong-device" ? $"other-{deviceId}" : deviceId,
                Target = DevicePrintTarget.General,
                JobId = workItemId,
                Revision = PrinterUpdateRevisions.Original,
                JobType = receiptKind == "original-order-job"
                    ? DevicePrintJobType.Order : DevicePrintJobType.Update,
                Status = DevicePrintStatus.Printed,
                ReceivedAt = TestNow,
                PrintedAt = TestNow,
                Copies = 1,
                CreatedBy = nameof(KitchenBoardWorkflowTests),
            });
            await context.SaveChangesAsync();
        }

        AuthenticateAsRole(UserRole.KitchenStaff);
        var feed = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(
            await Client.GetAsync("/api/staff/kitchen-board/work?pageSize=10")))!.Data!;
        feed.Corrections.Items.Should().ContainSingle(item => item.WorkItemId == workItemId
            && !item.IsCompleted, receiptKind);
    }

    [Fact]
    public async Task Correction_watermark_does_not_skip_a_writer_behind_an_uncommitted_change()
    {
        var orderId = await SeedOrderAsync(OrderStatus.Cancelled, DevicePrintStatus.NotConfigured);
        var firstWorkItemId = Guid.NewGuid();
        var secondWorkItemId = Guid.NewGuid();

        await using var firstWriter = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await firstWriter.OpenAsync();
        await using var firstTransaction = await firstWriter.BeginTransactionAsync();
        await InsertCorrectionAsync(firstWriter, firstTransaction, orderId, firstWorkItemId);

        var secondApplicationName = $"kitchen-board-cursor-{Guid.NewGuid():N}";
        var secondWriterConnectionString = new NpgsqlConnectionStringBuilder(DatabaseFixture.ConnectionString)
        {
            ApplicationName = secondApplicationName,
        }.ConnectionString;
        var secondWriterPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWriter = InsertWithSecondWriterAsync(
            secondWriterConnectionString, secondWriterPid, orderId, secondWorkItemId);
        var firstWriterCommitted = false;

        try
        {
            var secondPid = await secondWriterPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            (await WaitForAdvisoryLockWaitAsync(secondPid)).Should().BeTrue(
                "a later note writer must not allocate/commit a higher sequence while the earlier writer is open");

            AuthenticateAsRole(UserRole.KitchenStaff);
            var pendingRead = Client.GetAsync("/api/staff/kitchen-board/work?pageSize=10");
            (await WaitForReaderSequenceLockWaitAsync()).Should().BeTrue(
                "a board watermark must wait until the uncommitted sequence owner commits or rolls back");

            await firstTransaction.CommitAsync();
            firstWriterCommitted = true;
            var firstResponse = await pendingRead.WaitAsync(TimeSpan.FromSeconds(10));
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var firstFeed = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(firstResponse))!.Data!;
            var firstVisible = firstFeed.Corrections.Items.Select(item => item.WorkItemId).ToHashSet();

            await secondWriter.WaitAsync(TimeSpan.FromSeconds(10));
            if (!firstVisible.Contains(secondWorkItemId))
            {
                var incremental = await Client.GetAsync(
                    $"/api/staff/kitchen-board/work?pageSize=10&correctionsCursor={Uri.EscapeDataString(firstFeed.Corrections.NextCursor!)}");
                incremental.StatusCode.Should().Be(HttpStatusCode.OK);
                var nextFeed = (await ReadResponseAsync<ApiResponse<KitchenBoardWorkFeedDto>>(incremental))!.Data!;
                firstVisible.UnionWith(nextFeed.Corrections.Items.Select(item => item.WorkItemId));
            }

            firstVisible.Should().Contain(firstWorkItemId);
            firstVisible.Should().Contain(secondWorkItemId,
                "a later transaction may commit after the first watermark, but its sequence must be fetchable as a delta");
        }
        finally
        {
            if (!firstWriterCommitted)
                await firstTransaction.RollbackAsync();
            if (!secondWriter.IsCompleted)
            {
                try { await secondWriter.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { }
            }
        }
    }

    private async Task<Guid> SeedOrderAsync(OrderStatus status, DevicePrintStatus routeStatus)
    {
        var orderId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        var order = NewOrder(orderId, status);
        order.RoutingStates.Add(NewRoute(orderId, routeStatus));
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return orderId;
    }

    private async Task<int> GetOrderVersionAsync(Guid orderId)
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.Orders.Where(order => order.Id == orderId)
            .Select(order => order.Version).SingleAsync();
    }

    private async Task ConfigureCorrectionRouteAsync(Guid orderId, string deviceId)
    {
        await using var context = DatabaseFixture.CreateContext();
        var route = await context.OrderRoutingStates.SingleAsync(state => state.OrderId == orderId
            && state.Target == DevicePrintTarget.General);
        route.DeviceId = deviceId;
        route.Status = DevicePrintStatus.Printed;
        await context.SaveChangesAsync();
    }

    private async Task<(Guid OrderId, OrderRoutingState Route, string DeviceId)> SeedConfiguredOrderAsync(
        DevicePrintStatus routeStatus)
    {
        var orderId = Guid.NewGuid();
        var route = NewRoute(orderId, routeStatus);
        route.Revision = 2;
        route.DeviceId = $"kitchen-board-{Guid.NewGuid():N}";
        await using var context = DatabaseFixture.CreateContext();
        var order = NewOrder(orderId, OrderStatus.Ready);
        order.RoutingStates.Add(route);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (orderId, route, route.DeviceId);
    }

    private async Task<Guid> SeedOrderWithCashierExceptionAsync()
    {
        var orderId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        var order = NewOrder(orderId, OrderStatus.Ready);
        order.RoutingStates.Add(NewRoute(orderId, DevicePrintStatus.NotConfigured));
        order.RoutingStates.Add(new OrderRoutingState
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            JobId = Guid.NewGuid(),
            Revision = 1,
            Target = DevicePrintTarget.Cashier,
            Status = DevicePrintStatus.Failed,
            IsRequired = true,
            DeviceId = "cashier-device",
            CreatedAt = TestNow,
            UpdatedAt = TestNow,
            CreatedBy = nameof(KitchenBoardWorkflowTests),
        });
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return orderId;
    }

    private async Task<Guid> SeedActiveCorrectionAsync(Guid orderId)
    {
        var workItemId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        context.OrderOperationalNotes.Add(new OrderOperationalNote
        {
            Id = workItemId,
            OrderId = orderId,
            Audience = OrderNoteAudience.Kitchen,
            ClientOperationId = Guid.NewGuid(),
            Text = "Add one prepared item",
            AmendmentId = Guid.NewGuid(),
            AccountRevision = 1,
            KitchenTarget = DevicePrintTarget.General,
            KitchenChangesJson = KitchenChangeSnapshot.Serialize(
            [new PrinterFeedChangeDto
            {
                Kind = KitchenChangeKind.Add,
                Current = new OrderItemDto
                {
                    Id = Guid.NewGuid(),
                    ProductName = "Soup",
                    Quantity = 1,
                }
            }]),
            CreatedAt = TestNow,
            CreatedBy = nameof(KitchenBoardWorkflowTests),
        });
        await context.SaveChangesAsync();
        return workItemId;
    }

    private async Task<(Guid OrderId, Guid WorkItemId, Guid SessionId)> SeedTerminalCorrectionAsync()
    {
        var orderId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        var order = NewOrder(orderId, OrderStatus.Cancelled);
        order.Type = OrderType.DineIn;
        order.TableId = tableId;
        order.TableNumber = 8;
        order.TableLabel = "T-8";
        order.ServiceSessionId = sessionId;
        order.Version = 6;
        order.RoutingStates.Add(NewRoute(orderId, DevicePrintStatus.NotConfigured));
        context.Tables.Add(new Table
        {
            Id = tableId,
            TableNumber = "T-8",
            MaxGuests = 4,
            IsActive = true,
            CreatedBy = nameof(KitchenBoardWorkflowTests),
        });
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = sessionId,
            TableId = tableId,
            TableNumber = 8,
            Currency = "CHF",
            Status = TableServiceSessionStatus.Open,
            Version = 19,
            AccountRevision = 15,
            OpenedAt = TestNow.AddHours(-1),
            CreatedBy = nameof(KitchenBoardWorkflowTests),
        });
        context.Orders.Add(order);
        context.OrderOperationalNotes.Add(new OrderOperationalNote
        {
            Id = workItemId,
            OrderId = orderId,
            Audience = OrderNoteAudience.Kitchen,
            ClientOperationId = Guid.NewGuid(),
            Text = "Cancel one prepared item",
            AmendmentId = Guid.NewGuid(),
            AccountRevision = 14,
            KitchenTarget = DevicePrintTarget.General,
            KitchenChangesJson = KitchenChangeSnapshot.Serialize(
            [new PrinterFeedChangeDto
            {
                Kind = KitchenChangeKind.Void,
                Previous = new OrderItemDto
                {
                    Id = Guid.NewGuid(),
                    ProductName = "Soup",
                    Quantity = 1,
                    SpecialInstructions = "leave at the side door"
                }
            }]),
            CreatedAt = TestNow,
            CreatedBy = nameof(KitchenBoardWorkflowTests),
        });
        await context.SaveChangesAsync();
        return (orderId, workItemId, sessionId);
    }

    private async Task<ApiResponse<KitchenBoardWorkCompletionDto>> CompleteAsync(
        Guid orderId, Guid workItemId, string kind, int orderVersion, long? revision) =>
        (await ReadResponseAsync<ApiResponse<KitchenBoardWorkCompletionDto>>(await PostAsJsonAsync(
            $"/api/staff/kitchen-board/orders/{orderId}/work-items/{workItemId}/complete",
            new { kind, expectedOrderVersion = orderVersion, expectedAccountRevision = revision })))!;

    private async Task InsertWithSecondWriterAsync(
        string connectionString,
        TaskCompletionSource<int> connectionProcessId,
        Guid orderId,
        Guid workItemId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        connectionProcessId.TrySetResult(connection.ProcessID);
        await InsertCorrectionAsync(connection, transaction, orderId, workItemId);
        await transaction.CommitAsync();
    }

    private async Task InsertCorrectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid orderId,
        Guid workItemId)
    {
        var change = KitchenChangeSnapshot.Serialize(
        [new PrinterFeedChangeDto
        {
            Kind = KitchenChangeKind.Void,
            Previous = new OrderItemDto
            {
                Id = Guid.NewGuid(),
                ProductName = "Soup",
                Quantity = 1,
            }
        }]);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO "OrderOperationalNotes" (
                id, order_id, text, audience, client_operation_id, created_at, created_by,
                amendment_id, account_revision, kitchen_target, kitchen_changes_json)
            VALUES (
                @id, @order_id, 'Void one prepared item', 'Kitchen', @client_operation_id,
                @created_at, 'KitchenBoardWorkflowTests', @amendment_id, 1, 'General', @changes::jsonb)
            """, connection, transaction);
        command.Parameters.AddWithValue("id", workItemId);
        command.Parameters.AddWithValue("order_id", orderId);
        command.Parameters.AddWithValue("client_operation_id", Guid.NewGuid());
        command.Parameters.AddWithValue("created_at", TestNow);
        command.Parameters.AddWithValue("amendment_id", Guid.NewGuid());
        command.Parameters.AddWithValue("changes", change);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> WaitForAdvisoryLockWaitAsync(int processId)
    {
        await using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT wait_event_type = 'Lock' AND wait_event = 'advisory' FROM pg_stat_activity WHERE pid = @pid",
            connection);
        command.Parameters.AddWithValue("pid", processId);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await command.ExecuteScalarAsync() is true)
                return true;
            await Task.Delay(25);
        }

        return false;
    }

    private async Task<bool> WaitForReaderSequenceLockWaitAsync()
    {
        await using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database()
                    AND wait_event_type = 'Lock' AND wait_event = 'advisory'
                    AND query LIKE '%restaurant-system.order-change-sequence%')
            """, connection);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await command.ExecuteScalarAsync() is true)
                return true;
            await Task.Delay(25);
        }

        return false;
    }

    private static Order NewOrder(Guid id, OrderStatus status) => new()
    {
        Id = id,
        OrderNumber = $"KB-{id:N}"[..11],
        Type = OrderType.Takeaway,
        Status = status,
        PaymentStatus = PaymentStatus.Pending,
        Total = 25m,
        RemainingAmount = 25m,
        IsKitchenReleased = true,
        Version = 1,
        OrderDate = TestNow,
        CreatedBy = nameof(KitchenBoardWorkflowTests),
    };

    private static OrderRoutingState NewRoute(Guid orderId, DevicePrintStatus status) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        JobId = Guid.NewGuid(),
        Revision = 1,
        Target = DevicePrintTarget.General,
        Status = status,
        IsRequired = true,
        CreatedAt = TestNow,
        UpdatedAt = TestNow,
        CreatedBy = nameof(KitchenBoardWorkflowTests),
    };

    private sealed class KitchenBoardModules : ITenantModules
    {
        private static readonly string[] Enabled =
            [ModuleIds.Core, ModuleIds.KitchenBoard, ModuleIds.Server, ModuleIds.Cashier, ModuleIds.Printing];

        public bool IsEnforced => true;
        public IReadOnlyList<string> EnabledModules => Enabled;
        public bool IsEnabled(string moduleId) => Enabled.Contains(moduleId, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class KitchenBoardTestFeatures : ITenantFeatures
    {
        public bool Enabled { get; set; } = true;
        public bool ServerWorkspaceV2 => true;
        public bool TableAccountV1 => Enabled;
        public bool OrderAmendmentsV1 => Enabled;
        public bool TableGuestVisitsV1 => false;
        public bool TableVisitReadinessV1 => false;
        public bool TableAccountPaymentsV1 => false;
        public bool ServerAccountCollectionV1 => false;
        public bool TableGuestAccountPaymentsV1 => false;
        public bool EnforceSauceMinimum => false;
        public bool OptionSetMaterializationEnabled => false;
    }
}
