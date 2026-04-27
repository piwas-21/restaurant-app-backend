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

namespace RestaurantSystem.Api.Features.Menus.Commands.CreateMenuBundleCommand;

public record CreateMenuBundleCommand(
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

public class CreateMenuBundleCommandValidator : AbstractValidator<CreateMenuBundleCommand>
{
    public CreateMenuBundleCommandValidator()
    {
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

public class CreateMenuBundleCommandHandler : ICommandHandler<CreateMenuBundleCommand, ApiResponse<ProductDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateMenuBundleCommandHandler> _logger;

    public CreateMenuBundleCommandHandler(ApplicationDbContext context, ICurrentUserService currentUserService, ILogger<CreateMenuBundleCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<ProductDto>> Handle(CreateMenuBundleCommand command, CancellationToken cancellationToken)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (command.CategoryIds?.Any() == true)
            {
                var categories = await _context.Categories
                   .Where(c => command.CategoryIds.Contains(c.Id))
                   .ToListAsync(cancellationToken);
            }

            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = command.Name,
                Description = command.Description,
                BasePrice = command.BasePrice,
                IsActive = command.IsActive,
                IsSpecial = command.IsSpecial,
                IsAvailable = command.IsAvailable,
                PreparationTimeMinutes = command.PreparationTimeMinutes,
                Type = ProductType.Menu, // Hardcoded
                KitchenType = KitchenType.None, // Menus usually don't have kitchen type directly, or maybe FrontKitchen?
                DisplayOrder = command.DisplayOrder,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            _context.Products.Add(product);

            var displayOrder = 0;

            if (command.CategoryIds != null)
            {
                foreach (var categoryId in command.CategoryIds)
                {
                    var productCategory = new ProductCategory
                    {
                        CategoryId = categoryId,
                        IsPrimary = categoryId == command.PrimaryCategoryId,
                        DisplayOrder = displayOrder++,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };
                    _context.ProductCategories.Add(productCategory);
                    product.ProductCategories.Add(productCategory);
                }
            }

            var languageCodes = command.Content.Select(x => x.Key).ToList();
            var duplicateLanguageCodes = languageCodes.GroupBy(x => x)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateLanguageCodes.Any())
            {
                return ApiResponse<ProductDto>.Failure($"Duplicate language codes found: {string.Join(", ", duplicateLanguageCodes)}");
            }

            foreach (var (languageCode, description) in command.Content)
            {
                var productDescription = new ProductDescription
                {
                    Lang = languageCode,
                    Name = description.Name,
                    Description = description.Description,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };
                _context.ProductDescriptions.Add(productDescription);
                product.Descriptions.Add(productDescription);
            }

            // Add Menu Definition
            var menuDef = new MenuDefinition
            {
                ProductId = product.Id,
                IsAlwaysAvailable = command.MenuDefinition.IsAlwaysAvailable,
                StartTime = command.MenuDefinition.StartTime,
                EndTime = command.MenuDefinition.EndTime,
                AvailableMonday = command.MenuDefinition.AvailableMonday,
                AvailableTuesday = command.MenuDefinition.AvailableTuesday,
                AvailableWednesday = command.MenuDefinition.AvailableWednesday,
                AvailableThursday = command.MenuDefinition.AvailableThursday,
                AvailableFriday = command.MenuDefinition.AvailableFriday,
                AvailableSaturday = command.MenuDefinition.AvailableSaturday,
                AvailableSunday = command.MenuDefinition.AvailableSunday,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            _context.MenuDefinitions.Add(menuDef);

            if (command.MenuDefinition.Sections != null)
            {
                foreach (var sectionDto in command.MenuDefinition.Sections)
                {
                    var section = new MenuSection
                    {
                        MenuDefinition = menuDef,
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

            // Re-fetch to map to DTO (reusing MapToProductDto logic or similar)
            // Since we don't have access to private MapToProductDto from CreateProductCommandHandler,
            // we should duplicate it or make it public/shared.
            // For now, I'll duplicate the relevant parts for Menu Bundle.

            var createdProduct = await _context.Products
               .Include(p => p.ProductCategories)
                   .ThenInclude(pc => pc.Category)
               .Include(p => p.MenuDefinition)
                   .ThenInclude(md => md!.Sections)
                       .ThenInclude(s => s.Items)
                           .ThenInclude(i => i.Product)
               .FirstAsync(p => p.Id == product.Id, cancellationToken);

            var productDto = MapToProductDto(createdProduct);

            _logger.LogInformation("Menu Bundle {ProductId} created successfully by user {UserId}",
                    product.Id, _currentUserService.UserId);

            return ApiResponse<ProductDto>.SuccessWithData(productDto, "Menu Bundle created successfully");
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
