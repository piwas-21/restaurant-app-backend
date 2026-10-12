using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateTipPreferencesCommand;

public sealed record UpdateTipPreferencesCommand : ICommand<ApiResponse<TipPreferencesDto>>
{
    [JsonRequired] public required string Mode { get; init; }
    [JsonRequired] public required int[] Values { get; init; }
    public string? Currency { get; init; }
}

public sealed class UpdateTipPreferencesCommandHandler(
    ApplicationDbContext context, ICurrentUserService currentUser)
    : ICommandHandler<UpdateTipPreferencesCommand, ApiResponse<TipPreferencesDto>>
{
    public async Task<ApiResponse<TipPreferencesDto>> Handle(
        UpdateTipPreferencesCommand command, CancellationToken cancellationToken)
    {
        var info = await context.RestaurantInfo.FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Restaurant info has not been initialised.");
        if (command.Mode == "fixed" && !string.Equals(info.Currency, command.Currency, StringComparison.Ordinal))
            throw new BadRequestException("Select the restaurant's current currency for fixed tips.");
        info.TipPresetMode = command.Mode;
        info.TipPresetValues = command.Values.ToArray();
        info.TipPresetCurrency = command.Mode == "fixed" ? command.Currency : null;
        info.UpdatedAt = DateTime.UtcNow;
        info.UpdatedBy = currentUser.GetAuditIdentifier();
        await context.SaveChangesAsync(cancellationToken);
        return ApiResponse<TipPreferencesDto>.SuccessWithData(
            new TipPreferencesDto(info.TipPresetMode, info.TipPresetValues, info.TipPresetCurrency));
    }
}
