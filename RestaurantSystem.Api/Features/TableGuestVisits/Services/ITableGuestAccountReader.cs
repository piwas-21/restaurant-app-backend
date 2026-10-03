using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public interface ITableGuestAccountReader
{
    Task<TableGuestAccountDto> ReadAsync(
        Guid serviceSessionId, string? participantToken, CancellationToken cancellationToken);
}
