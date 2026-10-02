using FluentValidation;
using System.Text.Json.Serialization;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Common;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Commands;

public sealed class CreateTableGuestRoundCommand : ICommand<ApiResponse<TableGuestAccountDto>>
{
    public Guid ServiceSessionId { get; init; }
    public Guid OperationId { get; init; }
    public long ExpectedAccountRevision { get; init; }
    public string ExpectedBasketFingerprint { get; init; } = string.Empty;

    [JsonIgnore]
    internal string BasketSessionId { get; init; } = string.Empty;

    [JsonIgnore]
    internal string? ParticipantToken { get; init; }
}

public sealed class CreateTableGuestRoundCommandValidator : AbstractValidator<CreateTableGuestRoundCommand>
{
    public CreateTableGuestRoundCommandValidator()
    {
        RuleFor(command => command.ServiceSessionId).NotEmpty();
        RuleFor(command => command.OperationId).NotEmpty();
        RuleFor(command => command.ExpectedAccountRevision).GreaterThan(0);
        RuleFor(command => command.ExpectedBasketFingerprint)
            .Must(BasketPurchaseFingerprint.IsValidDigest)
            .WithMessage("The reviewed basket fingerprint is invalid.");
        RuleFor(command => command.BasketSessionId).NotEmpty().MaximumLength(100);
        RuleFor(command => command.ParticipantToken)
            .Must(value => TableGuestCredentialCrypto.TryHashParticipantToken(value, out _))
            .WithMessage("Guest participant token is invalid.");
    }
}

public sealed class CreateTableGuestRoundCommandHandler
    : ICommandHandler<CreateTableGuestRoundCommand, ApiResponse<TableGuestAccountDto>>
{
    private readonly ITenantFeatures _features;
    private readonly CustomMediator _mediator;
    private readonly ITableGuestAccountReader _accountReader;

    public CreateTableGuestRoundCommandHandler(
        ITenantFeatures features, CustomMediator mediator, ITableGuestAccountReader accountReader)
    {
        _features = features;
        _mediator = mediator;
        _accountReader = accountReader;
    }

    public async Task<ApiResponse<TableGuestAccountDto>> Handle(
        CreateTableGuestRoundCommand command, CancellationToken cancellationToken)
    {
        if (!_features.TableGuestVisitsV1)
        {
            throw new NotFoundException("Guest access is unavailable for this table visit.");
        }

        if (!TableGuestCredentialCrypto.TryHashParticipantToken(command.ParticipantToken, out var tokenHash))
        {
            throw new NotFoundException("Guest access is unavailable for this table visit.");
        }

        var guestContext = new TableGuestRoundContext(
            command.ServiceSessionId, command.OperationId, command.ExpectedAccountRevision, tokenHash,
            TableGuestCredentialCrypto.HashBasketSession(command.BasketSessionId),
            command.ExpectedBasketFingerprint.ToUpperInvariant());
        var basketCommand = new CreateOrderFromBasketCommand
        {
            SessionId = command.BasketSessionId,
            Type = OrderType.DineIn,
            Tip = 0m,
            GuestRoundContext = guestContext,
        };
        var created = await _mediator.SendCommand(basketCommand, cancellationToken);
        if (!created.Success)
        {
            var response = ApiResponse<TableGuestAccountDto>.Failure(
                created.Errors ?? ["The guest round could not be created."],
                created.Message ?? ApiResponse<TableGuestAccountDto>.DefaultFailureMessage);
            response.ErrorCode = created.ErrorCode;
            return response;
        }

        var account = await _accountReader.ReadAsync(
            command.ServiceSessionId, command.ParticipantToken, cancellationToken);
        return ApiResponse<TableGuestAccountDto>.SuccessWithData(account, "Guest round added");
    }
}
