using FluentValidation;
using RestaurantSystem.Api.Features.Reservations.Dtos;

namespace RestaurantSystem.Api.Features.Reservations.Commands.PrepareCustomerReservationCancellationCommand;

public sealed class PrepareCustomerReservationCancellationCommandValidator : AbstractValidator<PrepareCustomerReservationCancellationCommand>
{
    public PrepareCustomerReservationCancellationCommandValidator()
    {
        RuleFor(value => value.Request).NotNull();
        When(value => value.Request is not null, () =>
        {
            RuleFor(value => value.Request.SchemaVersion).NotNull().Equal(1);
            RuleFor(value => value.Request.OperationId).NotEmpty();
            RuleFor(value => value.Request.TenantId).NotEmpty();
            RuleFor(value => value.Request.ReservationId).NotEmpty();
            RuleFor(value => value.Request.TimeZone).NotEmpty().MaximumLength(128);
        });
    }
}
