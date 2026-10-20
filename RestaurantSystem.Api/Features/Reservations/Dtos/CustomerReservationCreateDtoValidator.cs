using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public sealed class CustomerReservationCreateDtoValidator : AbstractValidator<CustomerReservationCreateDto>
{
    public CustomerReservationCreateDtoValidator()
    {
        RuleFor(value => value.SchemaVersion).NotNull().Equal(1);
        RuleFor(value => value.OperationId).NotEmpty();
        RuleFor(value => value.TenantId).NotEmpty();
        RuleFor(value => value.TimeZone).NotEmpty().MaximumLength(100);
        RuleFor(value => value.Date).NotNull();
        RuleFor(value => value.StartTime).NotNull().GreaterThanOrEqualTo(TimeSpan.Zero);
        RuleFor(value => value.EndTime).NotNull().LessThanOrEqualTo(TimeSpan.FromDays(1))
            .GreaterThan(value => value.StartTime);
        RuleFor(value => value.NumberOfGuests).NotNull().InclusiveBetween(1, 20);
        RuleFor(value => value.CustomerName).NotEmpty().MaximumLength(100);
        RuleFor(value => value.CustomerEmail).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(value => value.CustomerPhone).MaximumLength(20);
        RuleFor(value => value.SpecialRequests).MaximumLength(1000);
    }
}
