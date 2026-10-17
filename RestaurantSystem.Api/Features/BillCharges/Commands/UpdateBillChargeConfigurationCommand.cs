using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.BillCharges.Commands;

public sealed record UpdateBillChargeConfigurationCommand(string? Currency, List<BillChargeRuleDto> Rules)
    : ICommand<ApiResponse<BillChargeConfigurationDto>>;
public sealed class UpdateBillChargeConfigurationCommandHandler(ApplicationDbContext context)
    : ICommandHandler<UpdateBillChargeConfigurationCommand, ApiResponse<BillChargeConfigurationDto>>
{
    public async Task<ApiResponse<BillChargeConfigurationDto>> Handle(UpdateBillChargeConfigurationCommand command, CancellationToken cancellationToken)
    {
        var info = await context.RestaurantInfo.FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Restaurant settings are unavailable.");
        if (!string.Equals(info.Currency, command.Currency, StringComparison.Ordinal))
            throw new ConflictException("The restaurant currency changed. Refresh these settings.");
        BillChargeRules.Validate(command.Rules, info.Currency);
        info.BillChargeRulesJson = JsonSerializer.Serialize(command.Rules, BillChargeRules.JsonOptions);
        await context.SaveChangesAsync(cancellationToken);
        return ApiResponse<BillChargeConfigurationDto>.SuccessWithData(new(info.Currency, command.Rules));
    }
}
