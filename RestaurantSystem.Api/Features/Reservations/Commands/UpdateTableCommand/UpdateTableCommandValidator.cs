using FluentValidation;

namespace RestaurantSystem.Api.Features.Reservations.Commands.UpdateTableCommand;

public class UpdateTableCommandValidator : AbstractValidator<UpdateTableCommand>
{
    public UpdateTableCommandValidator()
    {
        RuleFor(x => x.TableId).NotEmpty().WithMessage("Table ID is required");
        RuleFor(x => x.TableData).NotNull().WithMessage("Table data is required");
        RuleFor(x => x.TableData.MaxGuests).GreaterThan(0).WithMessage("Max guests must be at least 1").When(x => x.TableData != null);
    }
}
