using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.CreateTableCommand;

public class CreateTableCommandValidator : AbstractValidator<CreateTableCommand>
{
    public CreateTableCommandValidator()
    {
        RuleFor(x => x.TableData).NotNull().WithMessage("Table data is required");
        RuleFor(x => x.TableData.TableNumber).NotEmpty().WithMessage("Table number is required").When(x => x.TableData != null);
        RuleFor(x => x.TableData.MaxGuests).GreaterThan(0).WithMessage("Max guests must be at least 1").When(x => x.TableData != null);
    }
}
