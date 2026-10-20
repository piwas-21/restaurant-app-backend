using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Services;

namespace RestaurantSystem.Api.Features.Reservations.Commands.ResolveCustomerReservationCommand;

public sealed class ResolveCustomerReservationCommandValidator : AbstractValidator<ResolveCustomerReservationCommand>
{
    public ResolveCustomerReservationCommandValidator()
    {
        RuleFor(value => value.Proof).NotNull();
        When(value => value.Proof is not null, () =>
        {
            RuleFor(value => value.Proof.SchemaVersion).NotNull().Equal(1);
            RuleFor(value => value.Proof.OperationId).NotEmpty();
            RuleFor(value => value.Proof.TenantId).NotEmpty();
            RuleFor(value => value.Proof.OwnerProof).NotEmpty().MaximumLength(CustomerReservationProofService.MaximumProofLength);
            RuleFor(value => value.Proof.PayloadHash).NotEmpty().Matches("^[a-f0-9]{64}$");
        });
    }
}
