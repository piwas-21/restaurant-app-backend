using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace RestaurantSystem.Api.Features.Menus.Queries.GetMenuBundleByIdQuery;

public record GetMenuBundleByIdQuery(Guid Id) : IQuery<ApiResponse<MenuBundleDto>>;

public class GetMenuBundleByIdQueryHandler(ApplicationDbContext context, IConfiguration configuration)
    : IQueryHandler<GetMenuBundleByIdQuery, ApiResponse<MenuBundleDto>>
{
    private readonly ApplicationDbContext _context = context;
    private readonly string _baseUrl = configuration["AWS:S3:BaseUrl"]!;

    public async Task<ApiResponse<MenuBundleDto>> Handle(GetMenuBundleByIdQuery query, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .Include(p => p.MenuDefinition)
                .ThenInclude(md => md!.Sections)
                    .ThenInclude(s => s.Items)
                        .ThenInclude(i => i.Product)
            .Include(p => p.Descriptions)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == query.Id && !p.IsDeleted, cancellationToken);

        if (product == null)
        {
            return ApiResponse<MenuBundleDto>.Failure("Menu bundle not found");
        }

        if (product.Type != ProductType.Menu)
        {
            return ApiResponse<MenuBundleDto>.Failure("Product is not a menu bundle");
        }

        var dto = MapToMenuBundleDto(product);
        return ApiResponse<MenuBundleDto>.SuccessWithData(dto);
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
                Sections = product.MenuDefinition.Sections.Select(s => new MenuSectionDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    DisplayOrder = s.DisplayOrder,
                    IsRequired = s.IsRequired,
                    MinSelection = s.MinSelection,
                    MaxSelection = s.MaxSelection,
                    Items = s.Items.Select(i => new MenuSectionItemDto
                    {
                        Id = i.Id,
                        ProductId = i.ProductId,
                        ProductName = i.Product?.Name,
                        AdditionalPrice = i.AdditionalPrice,
                        DisplayOrder = i.DisplayOrder,
                        IsDefault = i.IsDefault
                    }).OrderBy(i => i.DisplayOrder).ToList()
                }).OrderBy(s => s.DisplayOrder).ToList()
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
