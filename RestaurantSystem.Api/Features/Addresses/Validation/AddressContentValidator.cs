using System.Text;
using FluentValidation;
using RestaurantSystem.Api.Features.Addresses.Interfaces;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Addresses.Validation;

public abstract class AddressContentValidator<T> : AbstractValidator<T> where T : IAddressContent
{
    protected AddressContentValidator()
    {
        RuleFor(value => value.Label).NotEmpty().WithMessage("Label is required")
            .Must(value => Fits(value, AddressFieldLimits.Label)).WithMessage("Label exceeds the storage limit.");
        RuleFor(value => value.AddressLine1).NotEmpty().WithMessage("Address line 1 is required")
            .Must(value => Fits(value, AddressFieldLimits.Line)).WithMessage("Address line 1 exceeds the storage limit.");
        RuleFor(value => value.AddressLine2).Must(value => Fits(value, AddressFieldLimits.Line))
            .WithMessage("Address line 2 exceeds the storage limit.");
        RuleFor(value => value.City).NotEmpty().WithMessage("City is required")
            .Must(value => Fits(value, AddressFieldLimits.City)).WithMessage("City exceeds the storage limit.");
        RuleFor(value => value.State).Must(value => Fits(value, AddressFieldLimits.State))
            .WithMessage("State exceeds the storage limit.");
        RuleFor(value => value.PostalCode).NotEmpty().WithMessage("Postal code is required")
            .Must(value => Fits(value, AddressFieldLimits.PostalCode)).WithMessage("Postal code exceeds the storage limit.");
        RuleFor(value => value.Country).NotEmpty().WithMessage("Country is required")
            .Must(value => Fits(value, AddressFieldLimits.Country)).WithMessage("Country exceeds the storage limit.");
        RuleFor(value => value.Phone).Must(value => Fits(value, AddressFieldLimits.Phone))
            .WithMessage("Phone exceeds the storage limit.");
        RuleFor(value => value.DeliveryInstructions).Must(value => Fits(value, AddressFieldLimits.Instructions))
            .WithMessage("Delivery instructions exceed the storage limit.");
    }

    // PostgreSQL counts Unicode characters, whereas string.Length counts UTF-16 code units.
    // Text storage also refuses NUL even when a value is within its length bound.
    private static bool Fits(string? value, int maximum) => value is null
        || (!value.Contains('\0') && value.EnumerateRunes().Take(maximum + 1).Count() <= maximum);
}
