using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public interface ITableServiceSessionPaymentReplayResolver
{
    Task<ApiResponse<TableServiceSessionDto>?> ResolveAsync(
        AddTableServiceSessionPaymentCommand command, CancellationToken cancellationToken);
}
