using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.BillCharges.Services;

public interface IBillChargeService
{
    Task ApplyAsync(Order order, IReadOnlyList<BillChargeInputDto>? inputs, Guid operationId,
        string? currency, CancellationToken cancellationToken);
}
