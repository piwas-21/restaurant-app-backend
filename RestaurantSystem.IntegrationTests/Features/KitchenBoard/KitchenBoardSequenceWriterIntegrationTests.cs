using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Api.Features.KitchenBoard.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.KitchenBoard;

[Collection("Database Lane 3")]
public sealed class KitchenBoardSequenceWriterIntegrationTests : IntegrationTestBase
{
    public KitchenBoardSequenceWriterIntegrationTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Receipt_batch_touches_exact_mixed_note_tuples_and_replay_is_idempotent()
    {
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var generalId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var wrongOrderId = Guid.NewGuid();
        var wrongTargetId = Guid.NewGuid();
        var staffNoteId = Guid.NewGuid();
        var deviceId = $"board-{Guid.NewGuid():N}";

        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Orders.AddRange(NewOrder(firstOrderId), NewOrder(secondOrderId));
            seed.OrderOperationalNotes.AddRange(
                NewKitchenNote(generalId, firstOrderId, DevicePrintTarget.General),
                NewKitchenNote(defaultId, firstOrderId, DevicePrintTarget.Default),
                NewKitchenNote(wrongOrderId, secondOrderId, DevicePrintTarget.General),
                NewKitchenNote(wrongTargetId, secondOrderId, DevicePrintTarget.Cashier),
                NewStaffNote(staffNoteId, secondOrderId));
            seed.DeviceOrderReceipts.AddRange(
                NewReceipt(generalId, firstOrderId, DevicePrintTarget.General, deviceId),
                NewReceipt(defaultId, firstOrderId, DevicePrintTarget.Default, deviceId));
            await seed.SaveChangesAsync();
        }

        var before = await ReadSequencesAsync(
            generalId, defaultId, wrongOrderId, wrongTargetId, staffNoteId);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var affected = await KitchenBoardSequenceWriter.TouchCorrectionsAsync(context,
            [
                (firstOrderId, generalId, DevicePrintTarget.General),
                (firstOrderId, defaultId, DevicePrintTarget.Default),
                (firstOrderId, wrongOrderId, DevicePrintTarget.General),
                (secondOrderId, wrongTargetId, DevicePrintTarget.General),
                (secondOrderId, staffNoteId, DevicePrintTarget.General),
            ], CancellationToken.None);

            affected.Should().Be(2);
        }

        var afterMixedBatch = await ReadSequencesAsync(
            generalId, defaultId, wrongOrderId, wrongTargetId, staffNoteId);
        afterMixedBatch[generalId].Should().BeGreaterThan(before[generalId]);
        afterMixedBatch[defaultId].Should().BeGreaterThan(before[defaultId]);
        afterMixedBatch[wrongOrderId].Should().Be(before[wrongOrderId],
            "the supplied order id does not match the note's order");
        afterMixedBatch[wrongTargetId].Should().Be(before[wrongTargetId],
            "the supplied route target does not match the note's target");
        // ck_order_operational_notes_kitchen_change requires Kitchen audience whenever a note has
        // a kitchen target, so this valid Staff note has no target; keep the SQL audience guard too.
        afterMixedBatch[staffNoteId].Should().Be(before[staffNoteId],
            "non-kitchen notes cannot be advanced by the kitchen feed");
        afterMixedBatch.Values.Should().OnlyHaveUniqueItems();

        var acknowledgements = new[]
        {
            NewPrintedAck(firstOrderId, generalId, DevicePrintTarget.General),
            NewPrintedAck(firstOrderId, defaultId, DevicePrintTarget.Default),
        };
        await ApplyAcknowledgementsAsync(deviceId, acknowledgements, replay: false);
        var afterFirstReceiptBatch = await ReadSequencesAsync(generalId, defaultId);
        afterFirstReceiptBatch[generalId].Should().BeGreaterThan(afterMixedBatch[generalId]);
        afterFirstReceiptBatch[defaultId].Should().BeGreaterThan(afterMixedBatch[defaultId]);

        await ApplyAcknowledgementsAsync(deviceId, acknowledgements, replay: true);
        var afterReplay = await ReadSequencesAsync(generalId, defaultId);
        afterReplay.Should().BeEquivalentTo(afterFirstReceiptBatch,
            "replaying already-printed receipts must not emit another correction-feed change");
    }

    private async Task ApplyAcknowledgementsAsync(
        string deviceId, IReadOnlyCollection<PrintAckDto> acknowledgements, bool replay)
    {
        var updateCounter = new CorrectionSequenceUpdateCounter();
        await using var context = DatabaseFixture.CreateContext(updateCounter);
        await using var batch = await KitchenBoardReceiptBatch.BeginAsync(
            context, new EnabledKitchenBoardFeatures(), acknowledgements, CancellationToken.None);

        foreach (var acknowledgement in acknowledgements)
        {
            var receipt = await context.DeviceOrderReceipts.SingleAsync(value =>
                value.DeviceId == deviceId
                && value.JobId == acknowledgement.JobId
                && value.Target == acknowledgement.Target);
            batch.TrackPotentialCorrectionChange(receipt, acknowledgement);
            if (!replay)
            {
                receipt.Status = acknowledgement.Status;
                receipt.PrintedAt = acknowledgement.PrintedAt;
            }
        }

        await context.SaveChangesAsync();
        await batch.CommitAsync(CancellationToken.None);
        updateCounter.Count.Should().Be(replay ? 0 : 1,
            "all changed correction notes should advance in one SQL update, and an unchanged receipt retry should issue none");
    }

    private async Task<Dictionary<Guid, long>> ReadSequencesAsync(params Guid[] noteIds)
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.OrderOperationalNotes.AsNoTracking()
            .Where(note => noteIds.Contains(note.Id))
            .ToDictionaryAsync(note => note.Id, note => note.KitchenBoardSequence);
    }

    private static Order NewOrder(Guid id) => new()
    {
        Id = id,
        OrderNumber = $"KB-{id:N}"[..11],
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Pending,
        Total = 10m,
        RemainingAmount = 10m,
        OrderDate = DateTime.UtcNow,
        CreatedBy = nameof(KitchenBoardSequenceWriterIntegrationTests),
    };

    private static OrderOperationalNote NewKitchenNote(
        Guid id, Guid orderId, DevicePrintTarget target) => new()
        {
            Id = id,
            OrderId = orderId,
            Audience = OrderNoteAudience.Kitchen,
            ClientOperationId = Guid.NewGuid(),
            Text = "Prepare one item",
            AmendmentId = Guid.NewGuid(),
            AccountRevision = 1,
            KitchenTarget = target,
            KitchenChangesJson = "[]",
            CreatedBy = nameof(KitchenBoardSequenceWriterIntegrationTests),
        };

    private static OrderOperationalNote NewStaffNote(Guid id, Guid orderId) => new()
    {
        Id = id,
        OrderId = orderId,
        Audience = OrderNoteAudience.Staff,
        ClientOperationId = Guid.NewGuid(),
        Text = "Staff note",
        CreatedBy = nameof(KitchenBoardSequenceWriterIntegrationTests),
    };

    private static DeviceOrderReceipt NewReceipt(
        Guid jobId, Guid orderId, DevicePrintTarget target, string deviceId) => new()
        {
            DeviceId = deviceId,
            OrderId = orderId,
            JobId = jobId,
            Revision = PrinterUpdateRevisions.Original,
            JobType = DevicePrintJobType.Update,
            Target = target,
            Status = DevicePrintStatus.Sent,
            ReceivedAt = DateTime.UtcNow,
            Copies = 1,
            CreatedBy = nameof(KitchenBoardSequenceWriterIntegrationTests),
        };

    private static PrintAckDto NewPrintedAck(
        Guid orderId, Guid jobId, DevicePrintTarget target) => new(
            orderId, target, DevicePrintStatus.Printed, DateTime.UtcNow, DateTime.UtcNow,
            null, 1, jobId, PrinterUpdateRevisions.Original, DevicePrintJobType.Update);

    private sealed class CorrectionSequenceUpdateCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE \"OrderOperationalNotes\"", StringComparison.Ordinal))
                Count++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class EnabledKitchenBoardFeatures : ITenantFeatures
    {
        public bool ServerWorkspaceV2 => false;
        public bool TableAccountV1 => true;
        public bool OrderAmendmentsV1 => false;
        public bool TableGuestVisitsV1 => false;
        public bool TableVisitReadinessV1 => false;
        public bool TableAccountPaymentsV1 => false;
        public bool ServerAccountCollectionV1 => false;
        public bool TableGuestAccountPaymentsV1 => false;
        public bool EnforceSauceMinimum => false;
        public bool OptionSetMaterializationEnabled => false;
    }
}
