using FluentValidation;

namespace RestaurantSystem.Api.Features.Categories.Commands.ReorderCategoriesCommand;

public class ReorderCategoriesCommandValidator : AbstractValidator<ReorderCategoriesCommand>
{
    public ReorderCategoriesCommandValidator()
    {
        RuleFor(x => x.CategoryOrders).NotEmpty().WithMessage("Category orders list cannot be empty");
    }
}
