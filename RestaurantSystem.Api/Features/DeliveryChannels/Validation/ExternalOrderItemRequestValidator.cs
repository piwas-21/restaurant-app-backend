using FluentValidation;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Validation;

public sealed class ExternalOrderItemRequestValidator : AbstractValidator<ExternalOrderItemRequest>
{
    public ExternalOrderItemRequestValidator()
    {
        RuleFor(item => item.ProductId).NotEmpty();
        RuleFor(item => item.VariationId).NotEqual(Guid.Empty).When(item => item.VariationId.HasValue);
        RuleFor(item => item.Name).NotEmpty().MaximumLength(ExternalOrderLimits.ItemNameLength).Must(ExternalOrderRequestValidator.IsText);
        RuleFor(item => item.VariationName).MaximumLength(ExternalOrderLimits.VariationNameLength).Must(ExternalOrderRequestValidator.IsText);
        RuleFor(item => item.VariationName).Empty().When(item => !item.VariationId.HasValue);
        RuleFor(item => item.Quantity).InclusiveBetween(1, ExternalOrderLimits.MaxQuantity);
        RuleFor(item => item.UnitPrice).InclusiveBetween(0, ExternalOrderLimits.MaxMoney).Must(ExternalOrderRequestValidator.IsMoney);
        RuleFor(item => item.Total).InclusiveBetween(0, ExternalOrderLimits.MaxMoney).Must(ExternalOrderRequestValidator.IsMoney)
            .Must((item, total) => item.UnitPrice is >= 0 and <= ExternalOrderLimits.MaxMoney
                && item.Quantity is >= 1 and <= ExternalOrderLimits.MaxQuantity && total == item.UnitPrice * item.Quantity);
        RuleFor(item => item.Instructions).MaximumLength(ExternalOrderLimits.ItemInstructionsLength).Must(ExternalOrderRequestValidator.IsText);
    }
}
