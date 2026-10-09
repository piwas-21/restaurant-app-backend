using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

internal static class TableOccupancyRecoveryResponses
{
    internal static TableOccupancyRecoveryOperationDto ToDto(
        TableOccupancyRecoveryOperation operation,
        IReadOnlyCollection<TableOccupancyRecoveryDisposition> dispositions)
    {
        var ordered = dispositions.OrderBy(value => value.RecordedAt)
            .ThenBy(value => value.OrderId).ToArray();
        return new TableOccupancyRecoveryOperationDto(
            operation.Id,
            operation.TableId,
            operation.ServiceSessionId,
            operation.Reason,
            operation.RecordedAt,
            operation.VisitReleasedAt,
            operation.OutcomeReadinessState.ToString(),
            operation.OutcomeReadinessVersion,
            operation.OutcomeSessionVersion,
            operation.OutcomeAccountRevision,
            ordered.Count(value => value.Kind == TableOccupancyRecoveryDispositionKind.CancelledUnsent),
            ordered.Count(value => value.WasLegacyUnassigned
                && value.Kind == TableOccupancyRecoveryDispositionKind.ArchivedLegacyOccupancy),
            ordered.Count(value => value.Kind == TableOccupancyRecoveryDispositionKind.RetainedInPriorVisit),
            ordered.Where(value => value.Kind != TableOccupancyRecoveryDispositionKind.CancelledUnsent)
                .Sum(value => value.OriginalTotalPaid),
            ordered.Where(value => value.Kind != TableOccupancyRecoveryDispositionKind.CancelledUnsent)
                .Sum(TableServiceSessionCloseRules.Outstanding),
            ordered.Select(value => ToDto(value)).ToArray());
    }

    private static TableOccupancyRecoveryOrderDto ToDto(TableOccupancyRecoveryDisposition value) => new(
        value.OrderId,
        value.OrderNumber,
        value.Kind.ToString(),
        value.OriginalStatus.ToString(),
        value.OriginalPaymentStatus.ToString(),
        value.OriginalTotal,
        value.OriginalBillingCreditAmount,
        value.OriginalTotalPaid,
        value.OriginalRemainingAmount,
        value.WasLegacyUnassigned,
        value.WasKitchenReleased,
        value.HadRoutingHistory);
}
