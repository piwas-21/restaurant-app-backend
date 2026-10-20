using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public sealed class CustomerReservationProofDtoValidator : AbstractValidator<CustomerReservationProofDto>
{
    public CustomerReservationProofDtoValidator()
    {
        RuleFor(value => value.SchemaVersion).NotNull().Equal(1);
        RuleFor(value => value.OperationId).NotEmpty();
        RuleFor(value => value.TenantId).NotEmpty();
        RuleFor(value => value.OwnerProof).NotEmpty().MaximumLength(CustomerReservationProofService.MaximumProofLength);
        RuleFor(value => value.PayloadHash).NotEmpty().Matches("^[a-f0-9]{64}$");
    }
}
