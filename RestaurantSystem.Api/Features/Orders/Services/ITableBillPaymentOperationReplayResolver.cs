using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface ITableBillPaymentOperationReplayResolver
{
    /// <summary>Returns null for a new operation, otherwise the committed outcome or a payload-reuse refusal.</summary>
    Task<ApiResponse<TableBillDto>?> ResolveAsync(
        AddTableBillPaymentCommand command, CancellationToken cancellationToken);
}
