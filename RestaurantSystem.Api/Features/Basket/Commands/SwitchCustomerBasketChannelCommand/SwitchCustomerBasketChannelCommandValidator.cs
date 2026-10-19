using FluentValidation;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Basket.Commands.SwitchCustomerBasketChannelCommand;

public sealed class SwitchCustomerBasketChannelCommandValidator : AbstractValidator<SwitchCustomerBasketChannelCommand>
{
    public SwitchCustomerBasketChannelCommandValidator(IOptions<BasketSettings> settings)
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.Request.OperationId).NotEmpty();
        RuleFor(command => command.Request.OrderType).IsInEnum();
        RuleFor(command => command.Request.ExpectedPurchaseFingerprint)
            .Must((command, fingerprint) => command.Request.ExpectedBasketId == Guid.Empty
                ? fingerprint == string.Empty : BasketPurchaseFingerprint.IsValidDigest(fingerprint));
        RuleFor(command => command.Request.ConfirmedConflictItemIds).NotNull()
            .Must(ids => ids is not null && ids.Count <= settings.Value.MaxItemsPerBasket
                && ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count);
    }
}
