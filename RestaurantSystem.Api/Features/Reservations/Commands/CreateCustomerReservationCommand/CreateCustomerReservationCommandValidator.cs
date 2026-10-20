using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateCustomerReservationCommand;

public sealed class CreateCustomerReservationCommandValidator : AbstractValidator<CreateCustomerReservationCommand>
{
    public CreateCustomerReservationCommandValidator()
    {
        RuleFor(value => value.Request).NotNull();
        When(value => value.Request is not null, () =>
        {
            RuleFor(value => value.Request.SchemaVersion).NotNull().Equal(1);
            RuleFor(value => value.Request.OperationId).NotEmpty();
            RuleFor(value => value.Request.TenantId).NotEmpty();
            RuleFor(value => value.Request.TimeZone).NotEmpty().MaximumLength(100);
            RuleFor(value => value.Request.Date).NotNull();
            RuleFor(value => value.Request.StartTime).NotNull().GreaterThanOrEqualTo(TimeSpan.Zero);
            RuleFor(value => value.Request.EndTime).NotNull().LessThanOrEqualTo(TimeSpan.FromDays(1))
                .GreaterThan(value => value.Request.StartTime);
            RuleFor(value => value.Request.NumberOfGuests).NotNull().InclusiveBetween(1, 20);
            RuleFor(value => value.Request.CustomerName).NotEmpty().MaximumLength(100);
            RuleFor(value => value.Request.CustomerEmail).NotEmpty().EmailAddress().MaximumLength(255);
            RuleFor(value => value.Request.CustomerPhone).MaximumLength(20);
            RuleFor(value => value.Request.SpecialRequests).MaximumLength(1000);
        });
    }
}
