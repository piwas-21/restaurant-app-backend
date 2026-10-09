using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public sealed class TableGuestAccountReader : ITableGuestAccountReader
{
    private const string UnavailableMessage = "Guest access is unavailable for this table visit.";
    private const int MaxItemDepth = 20;
    private readonly ApplicationDbContext _context;
    private readonly ITenantFeatures _features;
    private readonly ITableBillAssembler _bills;
    private readonly TimeProvider _timeProvider;

    public TableGuestAccountReader(
        ApplicationDbContext context,
        ITenantFeatures features,
        ITableBillAssembler bills,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _features = features;
        _bills = bills;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TableGuestAccountDto> ReadAsync(
        Guid serviceSessionId, string? participantToken, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (!TableGuestCredentialCrypto.TryHashParticipantToken(participantToken, out var tokenHash))
        {
            throw Unavailable();
        }

        TableGuestAccountDto account;
        await using (var snapshot = await _context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken))
        {
            var session = await _context.TableServiceSessions.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (session?.Status != TableServiceSessionStatus.Open || session.ReleasedAt.HasValue
                || !session.TableId.HasValue
                || !await HasActiveParticipantAsync(serviceSessionId, tokenHash, now, cancellationToken))
            {
                throw Unavailable();
            }

            var bill = await _bills.AssembleAsync(session.Id, cancellationToken);
            if (bill is not null)
            {
                account = Project(bill);
            }
            else
            {
                var label = await _context.Tables.AsNoTracking()
                    .Where(table => table.Id == session.TableId)
                    .Select(table => table.TableNumber)
                    .SingleOrDefaultAsync(cancellationToken);
                account = new TableGuestAccountDto(
                    session.Id, label ?? session.TableNumber?.ToString(), session.Currency,
                    session.AccountRevision, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m,
                    Array.Empty<TableGuestOrderDto>(), Array.Empty<TableGuestAccountLineDto>());
            }

            await snapshot.CommitAsync(cancellationToken);
        }

        // Recheck after disposing the snapshot. A close may have committed while bill assembly
        // ran, so a stale snapshot must never expose the prior visit after its credentials revoke.
        if (!await IsStillAuthorizedAsync(serviceSessionId, tokenHash, cancellationToken))
        {
            throw Unavailable();
        }

        return account;
    }

    private Task<bool> HasActiveParticipantAsync(
        Guid serviceSessionId, string tokenHash, DateTime now, CancellationToken cancellationToken) =>
        _context.Set<TableGuestParticipant>().AsNoTracking().AnyAsync(value =>
            value.ServiceSessionId == serviceSessionId
            && value.TokenHash == tokenHash
            && value.RevokedAt == null
            && value.ExpiresAt > now,
            cancellationToken);

    private Task<bool> IsStillAuthorizedAsync(
        Guid serviceSessionId, string tokenHash, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return _context.TableServiceSessions.AsNoTracking().AnyAsync(session =>
            session.Id == serviceSessionId
            && session.Status == TableServiceSessionStatus.Open && session.ReleasedAt == null
            && session.TableId.HasValue
            && _context.Set<TableGuestParticipant>().Any(participant =>
                participant.ServiceSessionId == serviceSessionId
                && participant.TokenHash == tokenHash
                && participant.RevokedAt == null
                && participant.ExpiresAt > now),
            cancellationToken);
    }

    internal static TableGuestAccountDto Project(TableBillDto bill)
    {
        if (!bill.AccountRevision.HasValue || !bill.ServiceSessionId.HasValue)
        {
            throw new InvalidOperationException("Guest bill projection requires an explicit visit account.");
        }

        var orders = bill.Orders.Select(order => new TableGuestOrderDto(
            order.Id, order.OrderNumber, order.Status, order.PaymentStatus, order.OrderDate,
            order.Total, order.TotalPaid, order.RemainingAmount)
        {
            PayableTotal = order.PayableTotal,
            BillingCreditAmount = order.BillingCreditAmount
        }).ToArray();
        var items = bill.AccountItems.Select(line => new TableGuestAccountLineDto(
            line.OrderId, line.OrderNumber, line.UnitCount, ProjectItem(line.ItemSnapshot, 0, []))).ToArray();

        return new TableGuestAccountDto(
            bill.ServiceSessionId.Value, bill.TableLabel, bill.Currency, bill.AccountRevision.Value,
            bill.SubTotal, bill.Tax, bill.Discount, bill.Tip, bill.Total, bill.TotalPaid,
            bill.Remaining, bill.Credit, orders, items)
        {
            OriginalTotal = bill.OriginalTotal,
            BillingCreditAmount = bill.BillingCreditAmount
        };
    }

    private static TableGuestItemDto ProjectItem(OrderItemDto item, int depth, HashSet<Guid> path)
    {
        var descend = depth < MaxItemDepth && path.Add(item.Id);
        var ingredients = (item.IngredientCustomizations ?? [])
            .Select(value => new TableGuestIngredientDto(
                value.IngredientName, value.Quantity, value.IsRemoved, value.IsAddOn))
            .ToArray();
        var sides = descend
            ? (item.SideItems ?? []).Select(side => ProjectItem(side, depth + 1, path)).ToArray()
            : Array.Empty<TableGuestItemDto>();
        if (descend)
        {
            path.Remove(item.Id);
        }

        return new TableGuestItemDto(
            item.Id, item.ProductName, item.VariationName, item.Quantity,
            item.UnitPrice, item.ItemTotal, ingredients, sides);
    }

    private void EnsureEnabled()
    {
        if (!_features.TableGuestVisitsV1)
        {
            throw Unavailable();
        }
    }

    private static NotFoundException Unavailable() => new(UnavailableMessage);
}
