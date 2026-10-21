using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Services;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateCustomerReservationCommand;

public sealed class CreateCustomerReservationCommandValidator : AbstractValidator<CreateCustomerReservationCommand>
{
    public CreateCustomerReservationCommandValidator()
    {
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerReservationCreateDtoValidator());
        RuleFor(value => value.Request.OwnerProof).NotEmpty().MaximumLength(CustomerReservationProofService.MaximumProofLength)
            .When(value => value.Request is not null);
        RuleFor(value => value.Request.PayloadHash).NotEmpty().Matches("^[a-f0-9]{64}$")
            .When(value => value.Request is not null);
    }
}
