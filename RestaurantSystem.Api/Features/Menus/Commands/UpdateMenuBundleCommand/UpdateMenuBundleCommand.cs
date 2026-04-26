using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Commands.UpdateMenuBundleCommand;

public record UpdateMenuBundleCommand(
    Guid Id,
    string Name,
    string? Description,
    decimal BasePrice,
    bool IsActive,
    bool IsAvailable,
    bool IsSpecial,
    int PreparationTimeMinutes,
    int DisplayOrder,
    List<Guid>? CategoryIds,
    Guid? PrimaryCategoryId,
    MenuDefinitionDto MenuDefinition,
    ProductDescriptionsDto Content
) : ICommand<ApiResponse<ProductDto>>;

public class UpdateMenuBundleCommandValidator : AbstractValidator<UpdateMenuBundleCommand>
{
    public UpdateMenuBundleCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Menu bundle name is required")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");

        RuleFor(x => x.BasePrice)
            .GreaterThan(0).WithMessage("Base price must be greater than 0");

        RuleFor(x => x.PreparationTimeMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Preparation time cannot be negative");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Display order cannot be negative");

        RuleFor(x => x.MenuDefinition)
            .NotNull().WithMessage("Menu definition is required");

        RuleFor(x => x.CategoryIds)
             .Must(x => x == null || x.Distinct().Count() == x.Count).WithMessage("Duplicate categories are not allowed");

        RuleFor(x => x.PrimaryCategoryId)
            .Must((command, primaryCategoryId) =>
                !primaryCategoryId.HasValue || (command.CategoryIds != null && command.CategoryIds.Contains(primaryCategoryId.Value)))
            .WithMessage("Primary category must be one of the selected categories");
    }
}

public class UpdateMenuBundleCommandHandler : ICommandHandler<UpdateMenuBundleCommand, ApiResponse<ProductDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateMenuBundleCommandHandler> _logger;

    public UpdateMenuBundleCommandHandler(ApplicationDbContext context, ICurrentUserService currentUserService, ILogger<UpdateMenuBundleCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<ProductDto>> Handle(UpdateMenuBundleCommand command, CancellationToken cancellationToken)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var product = await _context.Products
                .Include(p => p.ProductCategories)
                .Include(p => p.Descriptions)
                .Include(p => p.MenuDefinition)
                    .ThenInclude(md => md!.Sections)
                .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

            if (product == null)
            {
                return ApiResponse<ProductDto>.Failure("Menu bundle not found");
            }

            if (product.Type != ProductType.Menu)
            {
                return ApiResponse<ProductDto>.Failure("Product is not a menu bundle");
            }

            // Validate categories
            if (command.CategoryIds?.Any() == true)
            {
                var categoriesCount = await _context.Categories
                   .CountAsync(c => command.CategoryIds.Contains(c.Id), cancellationToken);

                if (categoriesCount != command.CategoryIds.Count)
                {
                    return ApiResponse<ProductDto>.Failure("One or more categories not found");
                }
            }

            // Update product properties
            product.Name = command.Name;
            product.Description = command.Description;
            product.BasePrice = command.BasePrice;
            product.IsActive = command.IsActive;
            product.IsSpecial = command.IsSpecial;
            product.IsAvailable = command.IsAvailable;
            product.PreparationTimeMinutes = command.PreparationTimeMinutes;
            product.DisplayOrder = command.DisplayOrder;
            product.UpdatedAt = DateTime.UtcNow;
            product.UpdatedBy = _currentUserService.GetAuditIdentifier();

            // Update Categories
            _context.ProductCategories.RemoveRange(product.ProductCategories);

            var displayOrder = 0;
            if (command.CategoryIds != null)
            {
                foreach (var categoryId in command.CategoryIds)
                {
                    var productCategory = new ProductCategory
                    {
                        ProductId = product.Id,
                        CategoryId = categoryId,
                        IsPrimary = categoryId == command.PrimaryCategoryId,
                        DisplayOrder = displayOrder++,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };
                    _context.ProductCategories.Add(productCategory);
                }
            }

            // Update Content (Descriptions)
            var languageCodes = command.Content.Select(x => x.Key).ToList();
            var duplicateLanguageCodes = languageCodes.GroupBy(x => x)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateLanguageCodes.Any())
            {
                return ApiResponse<ProductDto>.Failure($"Duplicate language codes found: {string.Join(", ", duplicateLanguageCodes)}");
            }

            _context.ProductDescriptions.RemoveRange(product.Descriptions);

            foreach (var (languageCode, description) in command.Content)
            {
                var productDescription = new ProductDescription
                {
                    ProductId = product.Id,
                    Lang = languageCode,
                    Name = description.Name,
                    Description = description.Description,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier(),
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = _currentUserService.GetAuditIdentifier()
                };
                _context.ProductDescriptions.Add(productDescription);
            }

            // Update Menu Definition
            var menuDef = product.MenuDefinition;
            if (menuDef == null)
            {
                menuDef = new MenuDefinition
                {
                    ProductId = product.Id,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };
                _context.MenuDefinitions.Add(menuDef);
            }

            menuDef.IsAlwaysAvailable = command.MenuDefinition.IsAlwaysAvailable;
            menuDef.StartTime = command.MenuDefinition.StartTime;
            menuDef.EndTime = command.MenuDefinition.EndTime;
            menuDef.AvailableMonday = command.MenuDefinition.AvailableMonday;
            menuDef.AvailableTuesday = command.MenuDefinition.AvailableTuesday;
            menuDef.AvailableWednesday = command.MenuDefinition.AvailableWednesday;
            menuDef.AvailableThursday = command.MenuDefinition.AvailableThursday;
            menuDef.AvailableFriday = command.MenuDefinition.AvailableFriday;
            menuDef.AvailableSaturday = command.MenuDefinition.AvailableSaturday;
            menuDef.AvailableSunday = command.MenuDefinition.AvailableSunday;
            menuDef.UpdatedAt = DateTime.UtcNow;
            menuDef.UpdatedBy = _currentUserService.GetAuditIdentifier();

            // Update Sections
            if (command.MenuDefinition.Sections != null)
            {
                // Remove existing sections
                if (menuDef.Sections != null)
                {
                    _context.MenuSections.RemoveRange(menuDef.Sections);
                }

                foreach (var sectionDto in command.MenuDefinition.Sections)
                {
                    var section = new MenuSection
                    {
                        MenuDefinition = menuDef, // EF Core will handle the ID link
                        Name = sectionDto.Name,
                        Description = sectionDto.Description,
                        DisplayOrder = sectionDto.DisplayOrder,
                        IsRequired = sectionDto.IsRequired,
                        MinSelection = sectionDto.MinSelection,
                        MaxSelection = sectionDto.MaxSelection,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };

                    _context.MenuSections.Add(section);

                    if (sectionDto.Items != null)
                    {
                        foreach (var itemDto in sectionDto.Items)
                        {
                            var item = new MenuSectionItem
                            {
                                MenuSection = section,
                                ProductId = itemDto.ProductId,
                                AdditionalPrice = itemDto.AdditionalPrice,
                                DisplayOrder = itemDto.DisplayOrder,
                                IsDefault = itemDto.IsDefault,
                                CreatedAt = DateTime.UtcNow,
                                CreatedBy = _currentUserService.GetAuditIdentifier()
                            };
                            _context.MenuSectionItems.Add(item);
                        }
                    }
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Re-fetch to map to DTO
            var updatedProduct = await _context.Products
                .Include(p => p.ProductCategories)
                    .ThenInclude(pc => pc.Category)
                .Include(p => p.MenuDefinition)
                    .ThenInclude(md => md!.Sections)
                        .ThenInclude(s => s.Items)
                            .ThenInclude(i => i.Product)
                .Include(p => p.Descriptions)
                .FirstAsync(p => p.Id == product.Id, cancellationToken);

            var productDto = MapToProductDto(updatedProduct);

            _logger.LogInformation("Menu Bundle {ProductId} updated successfully by user {UserId}",
                    product.Id, _currentUserService.UserId);

            return ApiResponse<ProductDto>.SuccessWithData(productDto, "Menu Bundle updated successfully");
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); } catch { }
            throw;
        }
    }

    private static ProductDto MapToProductDto(Product product)
    {
        var dto = new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            IsActive = product.IsActive,
            IsAvailable = product.IsAvailable,
            PreparationTimeMinutes = product.PreparationTimeMinutes,
            Type = product.Type,
            KitchenType = product.KitchenType,
            DisplayOrder = product.DisplayOrder,
            Categories = product.ProductCategories.Select(pc => new ProductCategoryDto
            {
                CategoryId = pc.CategoryId,
                CategoryName = pc.Category.Name,
                IsPrimary = pc.IsPrimary,
                DisplayOrder = pc.DisplayOrder
            }).ToList(),
            PrimaryCategory = product.ProductCategories
                .Where(pc => pc.IsPrimary)
                .Select(pc => new CategoryDto
                {
                    Id = pc.Category.Id,
                    Name = pc.Category.Name,
                    Description = pc.Category.Description,
                    ImageUrl = pc.Category.ImageUrl,
                    IsActive = pc.Category.IsActive,
                    DisplayOrder = pc.Category.DisplayOrder
                })
                .FirstOrDefault(),
            MenuDefinition = product.MenuDefinition != null ? new MenuDefinitionDto
            {
                Id = product.MenuDefinition.Id,
                IsAlwaysAvailable = product.MenuDefinition.IsAlwaysAvailable,
                StartTime = product.MenuDefinition.StartTime,
                EndTime = product.MenuDefinition.EndTime,
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
            Content = new()
        };

        foreach (var description in product.Descriptions)
        {
            dto.Content[description.Lang] = new ProductDescriptionDto
            {
                Name = description.Name,
                Description = description.Description
            };
        }
        return dto;
    }
}
