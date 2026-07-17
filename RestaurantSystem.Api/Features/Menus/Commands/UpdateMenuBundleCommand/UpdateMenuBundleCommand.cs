using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
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
) : ICommand<ApiResponse<ProductDto>>, IMenuBundleCommandFields;

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

            // Update Categories.
            //
            // An absent or empty CategoryIds means "no category instruction", NOT "clear them".
            // The RemoveRange used to run BEFORE this check, so null and [] both deleted every
            // assignment and re-added none — and since no client can populate the field for a
            // bundle (the bundle form has no category control and MenuBundleDto returns none),
            // every bundle update silently dropped its categories (#190).
            //
            // The condition deliberately matches the validation block above, which already read
            // `CategoryIds?.Any() == true` — empty already meant "nothing to validate" there, so
            // this only makes the mutation path agree with the check that guards it.
            //
            // There is deliberately no "clear all categories" payload: products cannot express
            // one either, since UpdateProductCommandValidator requires CategoryIds NotEmpty. If
            // clearing is ever wanted it needs an explicit opt-in, not an empty list — which is
            // indistinguishable from a client that simply has nothing to say.
            //
            // NOTE: skipping this block is only safe for PrimaryCategoryId because
            // MenuBundleCommandValidatorBase rejects a primary that isn't in CategoryIds — so
            // when we skip, PrimaryCategoryId is necessarily null and there is nothing to apply.
            // Relaxing that rule to allow re-pointing the primary without resending categories
            // would need this block to handle it, or the change would be silently ignored.
            if (command.CategoryIds?.Any() == true)
            {
                _context.ProductCategories.RemoveRange(product.ProductCategories);

                var displayOrder = 0;
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

            // Update Content (Descriptions).
            //
            // Content may be omitted or empty (e.g. an edit that does not touch translations);
            // treat that as "no translation changes" rather than NRE-ing on a null or wiping
            // every description. Mirrors UpdateProductCommandHandler, which already had both
            // guards — this handler had neither, so the same UI action meant "no-op" on a
            // product and "delete every translation" on a bundle (#190).
            var contentMap = command.Content ?? new ProductDescriptionsDto();

            var languageCodes = contentMap.Select(x => x.Key).ToList();
            var duplicateLanguageCodes = languageCodes.GroupBy(x => x)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateLanguageCodes.Any())
            {
                return ApiResponse<ProductDto>.Failure($"Duplicate language codes found: {string.Join(", ", duplicateLanguageCodes)}");
            }

            if (contentMap.Any())
            {
                _context.ProductDescriptions.RemoveRange(product.Descriptions);
            }

            foreach (var (languageCode, description) in contentMap)
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

            var productDto = ProductDtoMapper.MapToProductDto(updatedProduct);

            _logger.LogInformation("Menu Bundle {ProductId} updated successfully by user {UserId}",
                    product.Id, _currentUserService.UserId);

            return ApiResponse<ProductDto>.SuccessWithData(productDto, "Menu Bundle updated successfully");
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); }
            catch (Exception rollbackEx)
            {
                // The original exception is the actionable one and is rethrown
                // below; rollback failure here is logged but mustn't shadow it.
                _logger.LogWarning(rollbackEx, "Transaction rollback failed during menu bundle update");
            }
            throw;
        }
    }
}
