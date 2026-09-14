using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Devices;

/// <summary>Print-ack compatibility tests. Legacy order receipts and additive update jobs share the
/// endpoint but use separate filtered natural keys.</summary>
[Collection("Database Lane 4")]
public class PrintAckUpdateTests : IntegrationTestBase
{
    public PrintAckUpdateTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task General_and_default_targets_round_trip_for_update_jobs()
    {
        var deviceId = NewDeviceId();
        var orderId = Guid.NewGuid();
        var generalJobId = Guid.NewGuid();
        var defaultJobId = Guid.NewGuid();
        var body = UpdateBatch(
            Ack(orderId, "General", generalJobId),
            Ack(orderId, "Default", defaultJobId));

        (await PostAcks(body, deviceId)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var receipts = Db(scope).DeviceOrderReceipts
            .Where(receipt => receipt.DeviceId == deviceId)
            .OrderBy(receipt => receipt.Target)
            .ToList();
        receipts.Should().HaveCount(2);
        receipts.Select(receipt => receipt.Target)
            .Should().BeEquivalentTo([DevicePrintTarget.General, DevicePrintTarget.Default]);
        receipts.Select(receipt => receipt.JobType)
            .Should().OnlyContain(type => type == DevicePrintJobType.Update);
        receipts.Select(receipt => receipt.Revision).Should().OnlyContain(revision => revision == 1);
    }

    [Theory]
    [InlineData("Queued", "General")]
    [InlineData("Queued", "Default")]
    [InlineData("Sent", "General")]
    [InlineData("Sent", "Default")]
    [InlineData("NotConfigured", "General")]
    [InlineData("NotConfigured", "Default")]
    [InlineData("Unknown", "General")]
    [InlineData("Unknown", "Default")]
    public async Task Explicit_new_statuses_round_trip_for_general_and_default_targets(
        string status, string target)
    {
        var deviceId = NewDeviceId();
        var orderId = Guid.NewGuid();
        var body = UpdateBatch(AckWithStatus(orderId, target, status, Guid.NewGuid()));

        (await PostAcks(body, deviceId)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var receipt = Db(scope).DeviceOrderReceipts
            .Single(receipt => receipt.DeviceId == deviceId);
        receipt.Target.Should().Be(Enum.Parse<DevicePrintTarget>(target));
        receipt.Status.Should().Be(Enum.Parse<DevicePrintStatus>(status));
    }

    [Fact]
    public async Task Update_jobs_coexist_with_legacy_ack_and_same_batch_retries_are_idempotent()
    {
        var deviceId = NewDeviceId();
        var orderId = Guid.NewGuid();
        var firstJobId = Guid.NewGuid();
        var secondJobId = Guid.NewGuid();
        var body = UpdateBatch(
            LegacyAck(orderId, "FrontKitchen", "Printed"),
            Ack(orderId, "FrontKitchen", firstJobId),
            Ack(orderId, "FrontKitchen", secondJobId));

        (await PostAcks(body, deviceId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAcks(body, deviceId)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var receipts = Db(scope).DeviceOrderReceipts
            .Where(receipt => receipt.DeviceId == deviceId && receipt.OrderId == orderId)
            .ToList();
        receipts.Should().HaveCount(3);
        receipts.Count(receipt => receipt.JobId is null).Should().Be(1);
        receipts.Count(receipt => receipt.JobId is not null).Should().Be(2);
    }

    [Fact]
    public async Task Old_ack_without_job_fields_still_upserts_the_legacy_receipt()
    {
        var deviceId = NewDeviceId();
        var orderId = Guid.NewGuid();
        var first = LegacyAck(orderId, "Cashier", "Received");
        var second = LegacyAck(orderId, "Cashier", "Printed");

        (await PostAcks(new { acks = new[] { first } }, deviceId))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAcks(new { acks = new[] { second } }, deviceId))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var receipts = Db(scope).DeviceOrderReceipts
            .Where(receipt => receipt.DeviceId == deviceId && receipt.OrderId == orderId)
            .ToList();
        receipts.Should().ContainSingle();
        receipts[0].JobId.Should().BeNull();
        receipts[0].Status.Should().Be(DevicePrintStatus.Printed);
    }

    [Fact]
    public async Task Update_only_printed_receipt_does_not_hide_an_unprinted_order()
    {
        var orderId = Guid.NewGuid();
        var old = DateTime.UtcNow.AddHours(-1);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = Db(scope);
            var order = new Order
            {
                Id = orderId,
                OrderNumber = "ACK-UPDATE-MISSED",
                Type = OrderType.Takeaway,
                Status = OrderStatus.Confirmed,
                PaymentStatus = PaymentStatus.Pending,
                Total = 10m,
                OrderDate = old,
                CreatedBy = "test",
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            order.CreatedAt = old;
            order.OrderDate = old;
            await db.SaveChangesAsync();
            db.DeviceOrderReceipts.Add(new DeviceOrderReceipt
            {
                DeviceId = NewDeviceId(),
                OrderId = orderId,
                JobId = Guid.NewGuid(),
                Revision = 1,
                JobType = DevicePrintJobType.Update,
                Target = DevicePrintTarget.General,
                Status = DevicePrintStatus.Printed,
                ReceivedAt = old,
                PrintedAt = old,
                Copies = 1,
                CreatedBy = "test",
            });
            await db.SaveChangesAsync();
        }

        AuthenticateAsAdmin();
        var response = await Client.GetAsync("/api/devices/missed-orders?graceMinutes=15");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<List<MissedOrderDto>>>(response);
        body!.Data.Should().Contain(order => order.OrderId == orderId);
    }

    [Fact]
    public async Task A_job_id_cannot_be_rebound_to_another_order_across_revisions_or_targets()
    {
        var deviceId = NewDeviceId();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var body = UpdateBatch(
            Ack(firstOrderId, "General", jobId),
            AckWithRevision(secondOrderId, "Default", jobId, 2));

        var response = await PostAcks(body, deviceId);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var failure = await ReadResponseAsync<ApiResponse<bool>>(response);
        failure!.Success.Should().BeFalse();
        failure.Errors.Should().ContainSingle();

        using var scope = Factory.Services.CreateScope();
        Db(scope).DeviceOrderReceipts
            .Where(receipt => receipt.DeviceId == deviceId)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task A_persisted_job_id_cannot_be_rebound_and_keeps_the_original_receipt()
    {
        var deviceId = NewDeviceId();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        (await PostAcks(UpdateBatch(Ack(firstOrderId, "General", jobId)), deviceId))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PostAcks(
            UpdateBatch(AckWithRevision(secondOrderId, "Default", jobId, 2)), deviceId);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var failure = await ReadResponseAsync<ApiResponse<bool>>(response);
        failure!.Success.Should().BeFalse();
        failure.Errors.Should().ContainSingle();

        using var scope = Factory.Services.CreateScope();
        var receipts = Db(scope).DeviceOrderReceipts
            .Where(receipt => receipt.DeviceId == deviceId)
            .ToList();
        receipts.Should().ContainSingle();
        receipts[0].OrderId.Should().Be(firstOrderId);
        receipts[0].Target.Should().Be(DevicePrintTarget.General);
    }

    [Fact]
    public async Task Job_id_without_revision_or_type_is_rejected_instead_of_using_legacy_key()
    {
        var deviceId = NewDeviceId();
        var body = new
        {
            acks = new[]
            {
                new
                {
                    orderId = Guid.NewGuid(),
                    target = "General",
                    status = "Printed",
                    receivedAt = DateTime.UtcNow,
                    printedAt = DateTime.UtcNow,
                    failureReason = (string?)null,
                    copies = 1,
                    jobId = Guid.NewGuid(),
                },
            },
        };

        var response = await PostAcks(body, deviceId);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpResponseMessage> PostAcks(object body, string deviceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/print-acks")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(DeviceApiKeyHeader, TestPrinterApiKey);
        request.Headers.Add("X-Device-Id", deviceId);
        return await Client.SendAsync(request);
    }

    private ApplicationDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    private static string NewDeviceId() => "dev-" + Guid.NewGuid().ToString("N");

    private static object UpdateBatch(params object[] acks) => new { acks };

    private static object Ack(Guid orderId, string target, Guid jobId) =>
        AckWithRevision(orderId, target, jobId, 1);

    private static object AckWithRevision(Guid orderId, string target, Guid jobId, int revision) =>
        AckWithStatus(orderId, target, "Printed", jobId, revision);

    private static object AckWithStatus(
        Guid orderId, string target, string status, Guid jobId, int revision = 1) => new
        {
            orderId,
            target,
            status,
            receivedAt = DateTime.UtcNow,
            printedAt = DateTime.UtcNow,
            failureReason = (string?)null,
            copies = 1,
            jobId,
            revision,
            jobType = "Update",
        };

    private static object LegacyAck(Guid orderId, string target, string status) => new
    {
        orderId,
        target,
        status,
        receivedAt = DateTime.UtcNow,
        printedAt = DateTime.UtcNow,
        failureReason = (string?)null,
        copies = 1,
    };
}
