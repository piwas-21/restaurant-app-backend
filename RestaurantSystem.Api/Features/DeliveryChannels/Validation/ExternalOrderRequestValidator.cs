using FluentValidation;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Validation;

public sealed class ExternalOrderRequestValidator : AbstractValidator<ExternalOrderRequest>
{
    public ExternalOrderRequestValidator()
    {
        RuleFor(request => request.Provider).NotEmpty().Matches("^[a-z][a-z0-9-]{0,49}$");
        RuleFor(request => request.StoreId).NotEmpty().MaximumLength(200).Must(IsIdentifier);
        // Also stored in the existing tender's varchar(100) transaction reference.
        RuleFor(request => request.ExternalOrderId).NotEmpty().MaximumLength(100).Must(IsIdentifier);
        RuleFor(request => request.DisplayId).NotEmpty().MaximumLength(100).Must(IsIdentifier);
        RuleFor(request => request.CanonicalOrderHash).NotEmpty().Matches("^[a-f0-9]{64}$");
        RuleFor(request => request.Currency).NotEmpty().Must(currency => currency is "EUR" or "CHF");
        RuleFor(request => request.MerchantTotal).InclusiveBetween(0, ExternalOrderLimits.MaxMoney).Must(IsMoney);
        RuleFor(request => request.ReportedTax).GreaterThanOrEqualTo(0).LessThanOrEqualTo(ExternalOrderLimits.MaxMoney)
            .Must(tax => tax is null || IsMoney(tax.Value));
        RuleFor(request => request.PlacedAt).NotEqual(default(DateTimeOffset));
        // Other fulfilment modes require their address, cash and handover contracts first.
        RuleFor(request => request.FulfillmentType).Equal("DELIVERY_BY_UBER");
        RuleFor(request => request.CustomerName).MaximumLength(OrderFieldLimits.CustomerNameMaxLength).Must(IsText);
        RuleFor(request => request.CustomerPhone).MaximumLength(OrderFieldLimits.CustomerPhoneMaxLength).Must(IsText);
        RuleFor(request => request.Instructions).MaximumLength(OrderFieldLimits.NotesMaxLength).Must(IsText);
        RuleFor(request => request.Items).NotEmpty().Must(items => items is not null && items.Count <= ExternalOrderLimits.MaxItems);
        RuleForEach(request => request.Items).NotNull().SetValidator(new ExternalOrderItemRequestValidator());
    }

    internal static bool IsMoney(decimal value) => value == decimal.Round(value, 2);
    internal static bool IsText(string? value) => value is null || !value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
    private static bool IsIdentifier(string? value) => value is not null && value == value.Trim() && !value.Any(char.IsControl);
}
