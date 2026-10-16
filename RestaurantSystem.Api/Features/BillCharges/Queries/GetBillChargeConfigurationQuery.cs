using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.BillCharges.Queries;

public sealed record GetBillChargeConfigurationQuery : IQuery<ApiResponse<BillChargeConfigurationDto>>;
public sealed class GetBillChargeConfigurationQueryHandler(ApplicationDbContext context)
    : IQueryHandler<GetBillChargeConfigurationQuery, ApiResponse<BillChargeConfigurationDto>>
{
    public async Task<ApiResponse<BillChargeConfigurationDto>> Handle(GetBillChargeConfigurationQuery query, CancellationToken cancellationToken)
    {
        var info = await context.RestaurantInfo.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Restaurant settings are unavailable.");
        return ApiResponse<BillChargeConfigurationDto>.SuccessWithData(new(info.Currency, BillChargeRules.Read(info.BillChargeRulesJson)));
    }
}
