using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

/// <summary>Floor shortcuts navigate to the fresh guarded action surface.</summary>
internal static class ServerFloorActionProjection
{
    internal static List<string> Project(
        Table table,
        ServerFloorSessionSummaryDto? session,
        bool hasLegacyAmbiguity,
        bool hasLegacyOrders,
        int readyCount,
        bool hasCurrentReservation,
        UserRole? currentRole,
        bool tableVisitReadinessEnabled = false)
    {
        if (!table.IsActive)
        {
            return [];
        }

        if (session is null)
        {
            if (hasLegacyOrders || hasLegacyAmbiguity) return ["ReviewLegacy"];
            if (tableVisitReadinessEnabled && table.ReadinessState == TableReadinessState.NeedsReset)
            {
                return ["MarkTableReady"];
            }

            return hasCurrentReservation ? [] : ["StartTable"];
        }

        var actions = new List<string> { "AddRound", "ViewBill" };
        if (readyCount > 0)
        {
            actions.Add("OpenTasks");
        }

        if (session.CanCollect && currentRole is UserRole.Admin or UserRole.Cashier)
        {
            actions.Add("CollectPayment");
        }

        if (session.CanRequestPaymentHandoff)
        {
            actions.Add("RequestPaymentHandoff");
        }

        if (session.CanClose)
        {
            actions.Add("CloseVisit");
        }

        if (hasLegacyAmbiguity)
        {
            actions.Add("ReviewLegacy");
        }

        return actions;
    }
}
