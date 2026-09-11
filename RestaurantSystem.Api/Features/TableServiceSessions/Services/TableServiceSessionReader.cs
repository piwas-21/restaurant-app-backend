using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Reads session metadata and its immutable-member bill as one contract.</summary>
public sealed class TableServiceSessionReader : ITableServiceSessionReader
{
    private readonly ApplicationDbContext _context;
    private readonly ITableBillAssembler _bills;
    private readonly TimeProvider _timeProvider;

    public TableServiceSessionReader(
        ApplicationDbContext context, ITableBillAssembler bills, TimeProvider? timeProvider = null)
    {
        _context = context;
        _bills = bills;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TableServiceSessionDto?> ReadAsync(
        Guid serviceSessionId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var session = await _context.TableServiceSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var currency = CurrencyCode.Normalize(session.Currency);
        var bill = await _bills.AssembleAsync(serviceSessionId, cancellationToken)
            ?? new TableBillDto
            {
                TableNumber = session.TableNumber,
                ServiceSessionId = session.Id,
                ServiceSessionVersion = session.Version,
                Currency = currency,
                GeneratedAt = now,
            };

        // A bill assembled before the metadata read can only differ in its clock. Session metadata
        // is authoritative for version/currency, so overwrite those fields before returning.
        bill.ServiceSessionId = session.Id;
        bill.ServiceSessionVersion = session.Version;
        bill.Currency = currency;

        return new TableServiceSessionDto
        {
            ServiceSessionId = session.Id,
            TableNumber = session.TableNumber,
            Currency = currency,
            Status = session.Status.ToString(),
            Version = session.Version,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            RoundCount = bill.OrderCount,
            AgeMinutes = Math.Max(0, (int)(now - session.OpenedAt).TotalMinutes),
            Outstanding = bill.Remaining,
            Bill = bill,
        };
    }
    public async Task<IReadOnlyList<TableServiceSessionDto>> ReadActiveAsync(
        CancellationToken cancellationToken)
    {
        var ids = await _context.TableServiceSessions
            .AsNoTracking()
            .Where(value => value.Status == Domain.Common.Enums.TableServiceSessionStatus.Open)
            .OrderBy(value => value.TableNumber)
            .Select(value => value.Id)
            .ToListAsync(cancellationToken);
        var sessions = new List<TableServiceSessionDto>(ids.Count);
        foreach (var id in ids)
        {
            var session = await ReadAsync(id, cancellationToken);
            if (session is not null)
            {
                sessions.Add(session);
            }
        }
        return sessions;
    }

}
