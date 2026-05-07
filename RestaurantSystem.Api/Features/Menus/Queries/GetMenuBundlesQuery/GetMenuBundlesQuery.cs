using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Queries.GetMenuBundlesQuery;

public record GetMenuBundlesQuery(int Page, int PageSize, Guid? CategoryId = null, bool IncludeUnavailable = false) : IQuery<ApiResponse<PagedResult<MenuBundleDto>>>;

public class GetMenuBundlesQueryHandler(ApplicationDbContext context, IConfiguration configuration)
    : IQueryHandler<GetMenuBundlesQuery, ApiResponse<PagedResult<MenuBundleDto>>>
{
    private readonly ApplicationDbContext _context = context;
    private readonly string _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    // The original _logger field and its injection via the constructor are removed as per the primary constructor syntax in the provided change.

    public async Task<ApiResponse<PagedResult<MenuBundleDto>>> Handle(GetMenuBundlesQuery query, CancellationToken cancellationToken)
    {
        var queryable = _context.Products
            .Include(p => p.MenuDefinition)
                .ThenInclude(md => md!.Sections)
                    .ThenInclude(s => s.Items)
                        .ThenInclude(i => i.Product)
                            .ThenInclude(p => p!.DetailedIngredients)
                                .ThenInclude(di => di.Descriptions)
            .Include(p => p.MenuDefinition)
                .ThenInclude(md => md!.Sections)
                    .ThenInclude(s => s.Items)
                        .ThenInclude(i => i.Product)
                            .ThenInclude(p => p!.SuggestedSideItems)
                                .ThenInclude(si => si.SideItemProduct)
            .Include(p => p.Descriptions)
            .Include(p => p.Images)
            .Where(p => !p.IsDeleted && p.MenuDefinition != null);

        // Filter by schedule availability (only if not including unavailable)
        if (!query.IncludeUnavailable)
        {
            var now = DateTime.UtcNow;
            var currentDayOfWeek = now.DayOfWeek;
            var currentTime = now.TimeOfDay;

            queryable = queryable.Where(p =>
                p.MenuDefinition!.IsAlwaysAvailable || // Include if always available
                (
                    // Check if available on current day
                    (currentDayOfWeek == DayOfWeek.Monday && p.MenuDefinition.AvailableMonday) ||
                    (currentDayOfWeek == DayOfWeek.Tuesday && p.MenuDefinition.AvailableTuesday) ||
                    (currentDayOfWeek == DayOfWeek.Wednesday && p.MenuDefinition.AvailableWednesday) ||
                    (currentDayOfWeek == DayOfWeek.Thursday && p.MenuDefinition.AvailableThursday) ||
                    (currentDayOfWeek == DayOfWeek.Friday && p.MenuDefinition.AvailableFriday) ||
                    (currentDayOfWeek == DayOfWeek.Saturday && p.MenuDefinition.AvailableSaturday) ||
                    (currentDayOfWeek == DayOfWeek.Sunday && p.MenuDefinition.AvailableSunday)
                ) &&
                (
                    // Check if within time range (if times are set)
                    (p.MenuDefinition.StartTime == null && p.MenuDefinition.EndTime == null) ||
                    (p.MenuDefinition.StartTime != null && p.MenuDefinition.EndTime != null &&
                     currentTime >= p.MenuDefinition.StartTime && currentTime <= p.MenuDefinition.EndTime)
                )
            );
        }


        var totalCount = await queryable.CountAsync(cancellationToken);

        var products = await queryable
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = products.Select(MapToMenuBundleDto).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var result = new PagedResult<MenuBundleDto>(
            dtos,
            totalCount,
            query.Page,
            query.PageSize,
            totalPages
        );

        return ApiResponse<PagedResult<MenuBundleDto>>.SuccessWithData(result,
            $"Retrieved {products.Count} menu bundles");
    }

    private MenuBundleDto MapToMenuBundleDto(Product product)
    {
        var dto = new MenuBundleDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            IsActive = product.IsActive,
            IsAvailable = product.IsAvailable,
            IsSpecial = product.IsSpecial,
            PreparationTimeMinutes = product.PreparationTimeMinutes,
            Type = "menu",
            DisplayOrder = product.DisplayOrder,
            MenuDefinition = product.MenuDefinition != null ? new MenuDefinitionDto
            {
                Id = product.MenuDefinition.Id,
                IsAlwaysAvailable = product.MenuDefinition.IsAlwaysAvailable,
                StartTime = product.MenuDefinition.StartTime?.ToString(@"hh\:mm\:ss"),
                EndTime = product.MenuDefinition.EndTime?.ToString(@"hh\:mm\:ss"),
                AvailableMonday = product.MenuDefinition.AvailableMonday,
                AvailableTuesday = product.MenuDefinition.AvailableTuesday,
                AvailableWednesday = product.MenuDefinition.AvailableWednesday,
                AvailableThursday = product.MenuDefinition.AvailableThursday,
                AvailableFriday = product.MenuDefinition.AvailableFriday,
                AvailableSaturday = product.MenuDefinition.AvailableSaturday,
                AvailableSunday = product.MenuDefinition.AvailableSunday,
                Sections = product.MenuDefinition.Sections.OrderBy(s => s.DisplayOrder).Select(s => new MenuSectionDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    DisplayOrder = s.DisplayOrder,
                    IsRequired = s.IsRequired,
                    MinSelection = s.MinSelection,
                    MaxSelection = s.MaxSelection,
                    Items = s.Items.OrderBy(i => i.DisplayOrder).Select(i => new MenuSectionItemDto
                    {
                        Id = i.Id,
                        ProductId = i.ProductId,
                        ProductName = i.Product?.Name,
                        AdditionalPrice = i.AdditionalPrice,
                        DisplayOrder = i.DisplayOrder,
                        IsDefault = i.IsDefault,
                        Ingredients = i.Product != null ? (i.Product.DetailedIngredients.Any()
                            ? i.Product.DetailedIngredients.Where(di => di.IsActive).Select(di => di.Name).ToList()
                            : i.Product.Ingredients) : null,
                        Allergens = i.Product?.Allergens,
                        DetailedIngredients = i.Product?.DetailedIngredients
                            .Where(di => di.IsActive)
                            .OrderBy(di => di.DisplayOrder)
                            .Select(di => new ProductIngredientDto
                            {
                                Id = di.Id,
                                Name = di.Name,
                                IsOptional = di.IsOptional,
                                Price = di.Price,
                                IsIncludedInBasePrice = di.IsIncludedInBasePrice,
                                IsActive = di.IsActive,
                                DisplayOrder = di.DisplayOrder,
                                MaxQuantity = di.MaxQuantity,
                                Content = di.Descriptions?.ToDictionary(
                                    desc => desc.LanguageCode,
                                    desc => new ProductIngredientContentDto
                                    {
                                        Name = desc.Name,
                                        Description = desc.Description
                                    }
                                )
                            }).ToList(),
                        SuggestedSideItems = i.Product?.SuggestedSideItems
                            .OrderBy(si => si.DisplayOrder)
                            .Select(si => new SuggestedSideItemDto
                            {
                                Id = si.Id,
                                SideItemProductId = si.SideItemProductId,
                                SideItemProductName = si.SideItemProduct?.Name,
                                SideItemBasePrice = si.SideItemProduct?.BasePrice ?? 0,
                                IsRequired = si.IsRequired,
                                DisplayOrder = si.DisplayOrder
                            }).ToList()
                    }).ToList()
                }).ToList()
            } : null,
            Content = new(),
            Images = product.Images.Select(i => new RestaurantSystem.Api.Features.Products.Dtos.ProductImageDto
            {
                Id = i.Id,
                Url = _baseUrl + "/" + i.Url,
                AltText = i.AltText,
                IsPrimary = i.IsPrimary,
                SortOrder = i.SortOrder
            }).OrderBy(i => i.SortOrder).ToList()
        };

        foreach (var description in product.Descriptions)
        {
            dto.Content[description.Lang] = new MenuBundleContentDto
            {
                Name = description.Name,
                Description = description.Description
            };
        }
        return dto;
    }
}
