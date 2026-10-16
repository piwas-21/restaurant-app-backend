using FluentValidation;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Commands;

public sealed record JoinTableGuestVisitCommand(string QrCodeData)
    : ICommand<ApiResponse<TableGuestJoinDto>>;

public sealed class JoinTableGuestVisitCommandValidator : AbstractValidator<JoinTableGuestVisitCommand>
{
    public JoinTableGuestVisitCommandValidator()
    {
        RuleFor(command => command.QrCodeData).NotEmpty().MaximumLength(128);
    }
}

public sealed class JoinTableGuestVisitCommandHandler
    : ICommandHandler<JoinTableGuestVisitCommand, ApiResponse<TableGuestJoinDto>>
{
    private readonly ITableGuestJoinService _admissions;

    public JoinTableGuestVisitCommandHandler(ITableGuestJoinService admissions) =>
        _admissions = admissions;

    public async Task<ApiResponse<TableGuestJoinDto>> Handle(
        JoinTableGuestVisitCommand command, CancellationToken cancellationToken) =>
        ApiResponse<TableGuestJoinDto>.SuccessWithData(
            await _admissions.JoinAsync(command.QrCodeData, cancellationToken),
            "Joined table visit");
}
