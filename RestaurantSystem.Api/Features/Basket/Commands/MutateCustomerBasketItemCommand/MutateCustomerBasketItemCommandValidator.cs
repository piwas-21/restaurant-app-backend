using FluentValidation;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Basket.Commands.MutateCustomerBasketItemCommand;

public sealed class MutateCustomerBasketItemCommandValidator : AbstractValidator<MutateCustomerBasketItemCommand>
{
    public MutateCustomerBasketItemCommandValidator(IOptions<BasketSettings> settings)
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.BasketItemId).NotEmpty();
        RuleFor(command => command.Request.OperationId).NotEmpty();
        RuleFor(command => command.Request.ExpectedBasketId).NotEmpty();
        RuleFor(command => command.Request.ExpectedPurchaseFingerprint).Must(BasketPurchaseFingerprint.IsValidDigest);
        RuleFor(command => command.Request.Action).IsInEnum();
        RuleFor(command => command.Request.SpecialInstructions).MaximumLength(500);
        When(command => command.Request.Action == CustomerBasketItemAction.Update, () =>
        {
            RuleFor(command => command.Request.Quantity).NotNull()
                .GreaterThan(0).LessThanOrEqualTo(settings.Value.MaxQuantityPerItem);
        });
        When(command => command.Request.Action == CustomerBasketItemAction.Remove, () =>
        {
            RuleFor(command => command.Request.Quantity).Null();
            RuleFor(command => command.Request.SpecialInstructions).Null();
        });
    }
}
