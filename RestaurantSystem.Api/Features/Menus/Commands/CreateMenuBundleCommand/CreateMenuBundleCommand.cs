using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
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
    ProductDescriptionsDto Content,
    int? AvailableOrderTypes = null,
    // Nullable-with-default for positional-record compatibility, NOT for the leave-alone
    // semantics its sibling on the update command has: on create there is nothing stored to
    // leave alone, so this is assigned as given and null simply yields an unlabelled bundle.
    // See IMenuBundleCommandFields for the contract the two paths do and do not share.
    List<string>? Allergens = null
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
                AvailableOrderTypes = command.AvailableOrderTypes,
                Allergens = command.Allergens,
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

            // Absent sections were always harmless on a create — there is nothing to erase — so
            // this guard never cost anyone data the way its update-path twins did (#191). It is
            // rewritten anyway because the shared MenuDefinitionDto lost its initializer and the
            // shared MenuBundleCommandValidatorBase now requires the key on BOTH bundle commands:
            // leaving create silently tolerant would put a second, quieter contract on one DTO.
            var sections = command.MenuDefinition.Sections
                ?? throw new BadRequestException(MenuDefinitionDto.SectionsRequiredMessage);

            MenuSectionWriter.ReplaceSections(_context, menuDef, sections, _currentUserService.GetAuditIdentifier());

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Re-fetch with the navigations the shared ProductDtoMapper reads, then map.

            var createdProduct = await _context.Products
                .WithProductDtoNavigations()
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
