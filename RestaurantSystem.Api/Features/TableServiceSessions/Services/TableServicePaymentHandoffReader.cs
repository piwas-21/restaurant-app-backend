using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Reads the latest durable handoff without coupling session bill assembly to it.</summary>
public static class TableServicePaymentHandoffReader
{
    public static async Task<TableServicePaymentHandoffDto?> ReadLatestAsync(
        ApplicationDbContext context, TableServiceSession session, CancellationToken cancellationToken)
    {
        var rows = await context.TableServicePaymentHandoffs
            .AsNoTracking()
            .Where(handoff => handoff.ServiceSessionId == session.Id)
            .OrderByDescending(handoff => handoff.RequestedAt)
            .Take(1)
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : ToDto(rows[0], session);
    }

    public static async Task<Dictionary<Guid, TableServicePaymentHandoffDto>> ReadLatestManyAsync(
        ApplicationDbContext context, List<TableServiceSession> sessions,
        CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
        {
            return [];
        }

        var ids = sessions.Select(session => session.Id).ToArray();
        var rows = await context.TableServicePaymentHandoffs
            .AsNoTracking()
            .Where(handoff => ids.Contains(handoff.ServiceSessionId))
            .GroupBy(handoff => handoff.ServiceSessionId)
            .Select(group => group
                .OrderByDescending(handoff => handoff.RequestedAt)
                .ThenByDescending(handoff => handoff.Id)
                .First())
            .ToListAsync(cancellationToken);
        var byId = sessions.ToDictionary(session => session.Id);
        return rows
            .ToDictionary(handoff => handoff.ServiceSessionId,
                handoff => ToDto(handoff, byId[handoff.ServiceSessionId]));
    }

    private static TableServicePaymentHandoffDto ToDto(
        TableServicePaymentHandoff handoff, TableServiceSession session) => new()
        {
            HandoffId = handoff.Id,
            ServiceSessionId = handoff.ServiceSessionId,
            OperationId = handoff.OperationId,
            TableId = session.TableId,
            TableNumber = session.TableNumber,
            TableLabel = session.Table?.TableNumber
                ?? session.TableNumber?.ToString(CultureInfo.InvariantCulture)
                ?? string.Empty,
            ExpectedVersion = handoff.ExpectedVersion,
            RequestedAmount = handoff.RequestedAmount,
            RequestedCurrency = handoff.RequestedCurrency,
            Status = handoff.Status.ToString(),
            RequestedAt = handoff.RequestedAt,
            ResolvedAt = handoff.ResolvedAt,
            ResolvedPaymentOperationId = handoff.ResolvedPaymentOperationId,
            CancelledAt = handoff.CancelledAt
        };
}
