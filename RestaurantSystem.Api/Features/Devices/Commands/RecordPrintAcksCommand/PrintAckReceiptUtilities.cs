using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Devices.Commands.RecordPrintAcksCommand;

internal static class PrintAckReceiptUtilities
{
    internal static void ApplyAcknowledgement(DeviceOrderReceipt receipt, PrintAckDto ack)
    {
        receipt.Status = ack.Status;
        receipt.FailureReason = ack.FailureReason;
        receipt.Copies = ack.Copies;
        receipt.ReceivedAt = AsUtc(ack.ReceivedAt);
        receipt.PrintedAt = ack.PrintedAt.HasValue ? AsUtc(ack.PrintedAt.Value) : null;
    }

    // Client instants are UTC; Npgsql rejects a non-UTC Kind for `timestamptz`.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    internal static bool IsReceiptUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (pg.ConstraintName?.Contains("DeviceOrderReceipts", StringComparison.OrdinalIgnoreCase) == true
            || pg.ConstraintName?.Contains("device_order_receipts", StringComparison.OrdinalIgnoreCase) == true);
}
