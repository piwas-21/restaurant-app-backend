using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableServiceSessionPaymentOperationQuery;

public record GetTableServiceSessionPaymentOperationQuery(
    Guid ServiceSessionId, Guid OperationId)
    : IQuery<ApiResponse<TableServiceSessionPaymentOperationLookupDto>>;

public sealed class GetTableServiceSessionPaymentOperationQueryHandler
    : IQueryHandler<GetTableServiceSessionPaymentOperationQuery,
        ApiResponse<TableServiceSessionPaymentOperationLookupDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ITableServiceSessionReader _reader;
    private readonly IOrderMappingService _mapping;

    public GetTableServiceSessionPaymentOperationQueryHandler(
        ApplicationDbContext context,
        ITableServiceSessionReader reader,
        IOrderMappingService mapping)
    {
        _context = context;
        _reader = reader;
        _mapping = mapping;
    }

    public async Task<ApiResponse<TableServiceSessionPaymentOperationLookupDto>> Handle(
        GetTableServiceSessionPaymentOperationQuery query,
        CancellationToken cancellationToken)
    {
        // Scope by both identifiers. A caller who knows an operation key from another visit
        // receives the same empty answer as an unknown key.
        var operation = await _context.TableBillPaymentOperations
            .AsNoTracking()
            .Include(value => value.Payments)
            .SingleOrDefaultAsync(value => value.OperationId == query.OperationId
                && value.ServiceSessionId == query.ServiceSessionId, cancellationToken);
        if (operation is null)
        {
            return ApiResponse<TableServiceSessionPaymentOperationLookupDto>.SuccessWithData(
                Unknown(query.OperationId), "Payment operation is not known for this session");
        }

        var session = await _reader.ReadAsync(query.ServiceSessionId, cancellationToken);
        if (session is null)
        {
            // A foreign or missing session never turns into a partial operation disclosure.
            return ApiResponse<TableServiceSessionPaymentOperationLookupDto>.SuccessWithData(
                Unknown(query.OperationId), "Payment operation is not known for this session");
        }

        var payments = operation.Payments
            .Select(payment =>
            {
                var dto = _mapping.MapToOrderPaymentDto(payment);
                dto.OperationId ??= operation.OperationId;
                return dto;
            })
            .ToList();
        return ApiResponse<TableServiceSessionPaymentOperationLookupDto>.SuccessWithData(
            new TableServiceSessionPaymentOperationLookupDto
            {
                OperationId = query.OperationId,
                Status = "Committed",
                Session = session,
                Payments = payments,
            },
            "Payment operation retrieved");
    }

    private static TableServiceSessionPaymentOperationLookupDto Unknown(Guid operationId) =>
        new()
        {
            OperationId = operationId,
            Status = "Unknown",
            Session = null,
            Payments = Array.Empty<RestaurantSystem.Api.Features.Orders.Dtos.OrderPaymentDto>(),
        };
}
