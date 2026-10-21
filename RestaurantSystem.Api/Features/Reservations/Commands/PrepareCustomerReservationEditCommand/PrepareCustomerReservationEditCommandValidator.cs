using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Commands.PrepareCustomerReservationEditCommand;

public sealed class PrepareCustomerReservationEditCommandValidator : AbstractValidator<PrepareCustomerReservationEditCommand>
{
    public PrepareCustomerReservationEditCommandValidator()
    {
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerReservationEditDtoValidator());
        RuleFor(value => value.Request.PayloadHash).Null().When(value => value.Request is not null);
        RuleFor(value => value.Request.OwnerProof).Null().When(value => value.Request is not null);
    }
}
