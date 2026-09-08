using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetTableBillQuery;

/// <summary>
/// ONE bill for a dine-in table: every order the table placed this service
/// (still open), grouped per order, with bill-level sums. Guests order in
/// rounds; the till settles one bill, not one order per round.
/// </summary>
public record GetTableBillQuery(int TableNumber) : IQuery<ApiResponse<TableBillDto>>;

public class GetTableBillQueryHandler : IQueryHandler<GetTableBillQuery, ApiResponse<TableBillDto>>
{
    private readonly ITableBillAssembler _assembler;
    private readonly ILogger<GetTableBillQueryHandler> _logger;

    public GetTableBillQueryHandler(ITableBillAssembler assembler, ILogger<GetTableBillQueryHandler> logger)
    {
        _assembler = assembler;
        _logger = logger;
    }

    public async Task<ApiResponse<TableBillDto>> Handle(GetTableBillQuery query, CancellationToken cancellationToken)
    {
        var bill = await _assembler.AssembleAsync(query.TableNumber, cancellationToken);

        if (bill == null)
        {
            _logger.LogInformation("No open orders for table {TableNumber} — no bill", query.TableNumber);
            return ApiResponse<TableBillDto>.Failure($"No open orders found for table {query.TableNumber}");
        }

        return ApiResponse<TableBillDto>.SuccessWithData(bill, "Table bill retrieved successfully");
    }
}
