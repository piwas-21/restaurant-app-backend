using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public interface ITableGuestJoinService
{
    Task<TableGuestJoinDto> JoinAsync(string qrCodeData, CancellationToken cancellationToken);
}
