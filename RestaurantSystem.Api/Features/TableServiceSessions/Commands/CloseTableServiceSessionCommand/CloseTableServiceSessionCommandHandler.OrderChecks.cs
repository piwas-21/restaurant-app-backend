using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;

public sealed partial class CloseTableServiceSessionCommandHandler
{
    private async Task<ApiResponse<TableServiceSessionDto>?> CheckOrderBalancesAsync(
        TableServiceSession session, CancellationToken cancellationToken)
    {
        var legacyQuery = TableServiceSessionCloseRules.ForUnassignedSession(
            _context.Orders, _context.Set<TableOccupancyRecoveryDisposition>(),
            session.TableId, session.TableNumber);
        var legacyOrders = await legacyQuery
            .Where(order => !order.IsDeleted
                && order.Type == OrderType.DineIn
                && order.ServiceSessionId == null)
            .SelectCloseCharges()
            .ToListAsync(cancellationToken);
        var memberRows = await _context.Orders
            .Where(order => !order.IsDeleted && order.ServiceSessionId == session.Id)
            .SelectCloseCharges()
            .ToListAsync(cancellationToken);
        var memberStates = memberRows.Select(order => order.ToState()).ToList();
        var legacyStates = legacyOrders.Select(order => order.ToState()).ToList();
        var assessment = TableServiceSessionCloseRules.Assess(
            memberStates,
            legacyStates,
            _paymentTolerance);
        if (assessment.LegacyActiveOrderCount > 0)
        {
            return Ambiguous();
        }

        var unresolved = memberRows.Zip(memberStates)
            .Where(pair => TableServiceSessionCloseRules.IsUnresolvedMemberOrder(pair.Second))
            .Select(pair => pair.First.OrderNumber)
            .ToList();
        return assessment.Outstanding > _paymentTolerance || unresolved.Count > 0
            ? Unresolved(assessment.Outstanding, unresolved)
            : null;
    }
}
