using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetPendingTableServicePaymentHandoffsQuery;

public record GetPendingTableServicePaymentHandoffsQuery
    : IQuery<ApiResponse<List<TableServicePaymentHandoffDto>>>;

public sealed class GetPendingTableServicePaymentHandoffsQueryHandler
    : IQueryHandler<GetPendingTableServicePaymentHandoffsQuery,
        ApiResponse<List<TableServicePaymentHandoffDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly int _queueLimit;

    public GetPendingTableServicePaymentHandoffsQueryHandler(
        ApplicationDbContext context,
        IOptions<TableServiceSessionSettings> settings)
    {
        _context = context;
        _queueLimit = settings.Value.PendingPaymentHandoffQueueLimit;
    }

    public async Task<ApiResponse<List<TableServicePaymentHandoffDto>>> Handle(
        GetPendingTableServicePaymentHandoffsQuery query, CancellationToken cancellationToken)
    {
        var rows = await _context.TableServicePaymentHandoffs
            .AsNoTracking()
            .Where(handoff => handoff.Status == TableServicePaymentHandoffStatus.Requested)
            .OrderBy(handoff => handoff.RequestedAt)
            .ThenBy(handoff => handoff.Id)
            .Take(_queueLimit)
            .Select(handoff => new
            {
                HandoffId = handoff.Id,
                ServiceSessionId = handoff.ServiceSessionId,
                OperationId = handoff.OperationId,
                TableId = handoff.ServiceSession.TableId,
                TableNumber = handoff.ServiceSession.TableNumber,
                TableLabel = handoff.ServiceSession.Table == null
                    ? null
                    : handoff.ServiceSession.Table.TableNumber,
                ExpectedVersion = handoff.ExpectedVersion,
                RequestedAmount = handoff.RequestedAmount,
                RequestedCurrency = handoff.RequestedCurrency,
                Status = handoff.Status,
                RequestedAt = handoff.RequestedAt,
                ResolvedAt = handoff.ResolvedAt,
                ResolvedPaymentOperationId = handoff.ResolvedPaymentOperationId,
                CancelledAt = handoff.CancelledAt
            })
            .ToListAsync(cancellationToken);

        var handoffs = rows.Select(row => new TableServicePaymentHandoffDto
        {
            HandoffId = row.HandoffId,
            ServiceSessionId = row.ServiceSessionId,
            OperationId = row.OperationId,
            TableId = row.TableId,
            TableNumber = row.TableNumber,
            TableLabel = row.TableLabel
                ?? row.TableNumber?.ToString(CultureInfo.InvariantCulture)
                ?? string.Empty,
            ExpectedVersion = row.ExpectedVersion,
            RequestedAmount = row.RequestedAmount,
            RequestedCurrency = row.RequestedCurrency,
            Status = row.Status.ToString(),
            RequestedAt = row.RequestedAt,
            ResolvedAt = row.ResolvedAt,
            ResolvedPaymentOperationId = row.ResolvedPaymentOperationId,
            CancelledAt = row.CancelledAt
        }).ToList();

        return ApiResponse<List<TableServicePaymentHandoffDto>>.SuccessWithData(
            handoffs, "Pending payment handoffs retrieved");
    }
}
