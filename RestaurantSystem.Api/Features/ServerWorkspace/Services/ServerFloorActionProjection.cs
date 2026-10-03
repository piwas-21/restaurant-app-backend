using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Services;

internal sealed record ServerFloorActionContext(
    bool HasLegacyAmbiguity,
    bool HasLegacyOrders,
    int ReadyCount,
    bool HasCurrentReservation,
    UserRole? CurrentRole,
    bool TableVisitReadinessEnabled);

/// <summary>Floor shortcuts navigate to the fresh guarded action surface.</summary>
internal static class ServerFloorActionProjection
{
    internal static List<string> Project(
        Table table,
        ServerFloorSessionSummaryDto? session,
        ServerFloorActionContext context)
    {
        if (!table.IsActive) return [];

        return session is null
            ? ProjectWithoutSession(table, context)
            : ProjectWithSession(session, context);
    }

    private static List<string> ProjectWithoutSession(Table table, ServerFloorActionContext context)
    {
        if (context.HasLegacyOrders || context.HasLegacyAmbiguity) return ["ReviewLegacy"];
        if (context.TableVisitReadinessEnabled && table.ReadinessState == TableReadinessState.NeedsReset)
        {
            return ["MarkTableReady"];
        }

        return context.HasCurrentReservation ? [] : ["StartTable"];
    }

    private static List<string> ProjectWithSession(
        ServerFloorSessionSummaryDto session,
        ServerFloorActionContext context)
    {
        var actions = new List<string> { "AddRound", "ViewBill" };
        if (context.ReadyCount > 0)
        {
            actions.Add("OpenTasks");
        }

        if (session.CanCollect && context.CurrentRole is UserRole.Admin or UserRole.Cashier)
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

        if (context.HasLegacyAmbiguity)
        {
            actions.Add("ReviewLegacy");
        }

        return actions;
    }
}
