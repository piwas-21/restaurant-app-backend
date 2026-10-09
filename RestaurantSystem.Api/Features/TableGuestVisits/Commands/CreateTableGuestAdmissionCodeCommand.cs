using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Commands;

public sealed record CreateTableGuestAdmissionCodeCommand(Guid ServiceSessionId, bool PreferShortCode = false)
    : ICommand<ApiResponse<TableGuestAdmissionCodeDto>>;

public sealed class CreateTableGuestAdmissionCodeCommandHandler
    : ICommandHandler<CreateTableGuestAdmissionCodeCommand, ApiResponse<TableGuestAdmissionCodeDto>>
{
    private readonly ITableGuestAdmissionService _admissions;

    public CreateTableGuestAdmissionCodeCommandHandler(ITableGuestAdmissionService admissions) =>
        _admissions = admissions;

    public async Task<ApiResponse<TableGuestAdmissionCodeDto>> Handle(
        CreateTableGuestAdmissionCodeCommand command, CancellationToken cancellationToken) =>
        ApiResponse<TableGuestAdmissionCodeDto>.SuccessWithData(
            await _admissions.CreateCodeAsync(
                command.ServiceSessionId, cancellationToken, command.PreferShortCode),
            "Visit admission code created");
}
