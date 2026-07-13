using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
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
) : ICommand<ApiResponse<ProductDto>>, IMenuBundleCommandFields;

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

            // Re-fetch with the navigations the shared ProductDtoMapper reads, then map.
            // For now, I'll duplicate the relevant parts for Menu Bundle.

            var createdProduct = await _context.Products
               .Include(p => p.ProductCategories)
                   .ThenInclude(pc => pc.Category)
               .Include(p => p.MenuDefinition)
                   .ThenInclude(md => md!.Sections)
                       .ThenInclude(s => s.Items)
                           .ThenInclude(i => i.Product)
               .FirstAsync(p => p.Id == product.Id, cancellationToken);

            var productDto = ProductDtoMapper.MapToProductDto(createdProduct);

            _logger.LogInformation("Menu Bundle {ProductId} created successfully by user {UserId}",
                    product.Id, _currentUserService.UserId);

            return ApiResponse<ProductDto>.SuccessWithData(productDto, "Menu Bundle created successfully");
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); }
            catch (Exception rollbackEx)
            {
                // The original exception is the actionable one and is rethrown
                // below; rollback failure here is logged but mustn't shadow it.
                _logger.LogWarning(rollbackEx, "Transaction rollback failed during menu bundle create");
            }
            throw;
        }
    }
}
