using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Commands.PrepareCustomerReservationCommand;

public sealed class PrepareCustomerReservationCommandValidator : AbstractValidator<PrepareCustomerReservationCommand>
{
    public PrepareCustomerReservationCommandValidator()
    {
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerReservationCreateDtoValidator());
        RuleFor(value => value.Request.OwnerProof).Null().When(value => value.Request is not null);
        RuleFor(value => value.Request.PayloadHash).Null().When(value => value.Request is not null);
    }
}
