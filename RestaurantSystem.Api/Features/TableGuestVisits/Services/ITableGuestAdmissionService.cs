using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public interface ITableGuestAdmissionService
{
    Task<TableGuestAdmissionCodeDto> CreateCodeAsync(Guid serviceSessionId, CancellationToken cancellationToken);
    Task<TableGuestJoinDto> JoinAsync(string qrCodeData, string admissionCode, CancellationToken cancellationToken);
}
