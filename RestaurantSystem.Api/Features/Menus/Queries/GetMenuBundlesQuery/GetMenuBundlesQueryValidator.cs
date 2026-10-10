using FluentValidation;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Menus.Queries.GetMenuBundlesQuery;

public sealed class GetMenuBundlesQueryValidator : AbstractValidator<GetMenuBundlesQuery>
{
    public GetMenuBundlesQueryValidator(IOptions<CatalogSettings> catalogSettings)
    {
        var maximumPageSize = catalogSettings.Value.MaxPageSize;

        RuleFor(query => query.Page)
            .GreaterThan(0).WithMessage("Page must be greater than zero.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, maximumPageSize)
            .WithMessage($"Page size must be between 1 and {maximumPageSize}.");

        RuleFor(query => query)
            .Must(query => ((long)query.Page - 1) * query.PageSize <= int.MaxValue)
            .When(query => query.Page > 0 && query.PageSize > 0)
            .WithMessage("Requested page offset is too large.");
    }
}
