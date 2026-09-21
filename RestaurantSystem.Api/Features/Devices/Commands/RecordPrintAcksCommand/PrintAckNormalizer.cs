using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Features.Devices.Dtos;

namespace RestaurantSystem.Api.Features.Devices.Commands.RecordPrintAcksCommand;

internal static class PrintAckNormalizer
{
    internal static PrintAckDto NormalizeOrderAcknowledgement(PrintAckDto acknowledgement)
    {
        if (acknowledgement.JobType != DevicePrintJobType.Order
            || acknowledgement.Status != DevicePrintStatus.NotConfigured)
        {
            return acknowledgement;
        }

        return acknowledgement with
        {
            Status = DevicePrintStatus.Skipped,
            FailureReason = acknowledgement.FailureReason ?? "No printer configured."
        };
    }
}
