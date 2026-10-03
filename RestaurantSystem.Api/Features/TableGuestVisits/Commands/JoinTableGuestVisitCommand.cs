using FluentValidation;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Commands;

public sealed record JoinTableGuestVisitCommand(string QrCodeData, string AdmissionCode)
    : ICommand<ApiResponse<TableGuestJoinDto>>;

public sealed class JoinTableGuestVisitCommandValidator : AbstractValidator<JoinTableGuestVisitCommand>
{
    public JoinTableGuestVisitCommandValidator()
    {
        RuleFor(command => command.QrCodeData).NotEmpty().MaximumLength(128);
        RuleFor(command => command.AdmissionCode)
            .Must(value => TableGuestCredentialCrypto.TryNormalizeAdmissionCode(value, out _))
            .WithMessage("Enter the 10-character visit code.");
    }
}

public sealed class JoinTableGuestVisitCommandHandler
    : ICommandHandler<JoinTableGuestVisitCommand, ApiResponse<TableGuestJoinDto>>
{
    private readonly ITableGuestAdmissionService _admissions;

    public JoinTableGuestVisitCommandHandler(ITableGuestAdmissionService admissions) =>
        _admissions = admissions;

    public async Task<ApiResponse<TableGuestJoinDto>> Handle(
        JoinTableGuestVisitCommand command, CancellationToken cancellationToken) =>
        ApiResponse<TableGuestJoinDto>.SuccessWithData(
            await _admissions.JoinAsync(command.QrCodeData, command.AdmissionCode, cancellationToken),
            "Joined table visit");
}
