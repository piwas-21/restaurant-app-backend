using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>Storage-bound note validation shared by staff quote, create, and round requests.</summary>
public static class StaffCounterNotesRule
{
    public const string MaximumLengthMessage = "Notes cannot exceed {MaxLength} characters.";

    public static void ValidateNotesLength(this AbstractValidator<StaffCounterOrderRequest> validator)
    {
        validator.RuleFor(request => request.Notes)
            .MaximumLength(OrderFieldLimits.NotesMaxLength)
            .WithMessage(MaximumLengthMessage);
    }
}
