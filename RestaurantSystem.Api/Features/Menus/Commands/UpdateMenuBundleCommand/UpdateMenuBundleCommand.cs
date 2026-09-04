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
    ProductDescriptionsDto Content,
    int? AvailableOrderTypes = null,
    // Nullable and defaulted so an older client that sends nothing LEAVES THE LABELS ALONE
    // rather than clearing them. The frontend already put `allergens` in every bundle PUT
    // before this field existed, so the shipping order matters and is the reverse of the
    // usual: the client that seeds the stored value lands FIRST
    // (piwas-21/restaurant-app-frontend#704). A client that seeds nothing sends `[]`, and `[]`
    // is a real instruction — an admin who unticks every chip means it.
    List<string>? Allergens = null
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
            // Assigned UNCONDITIONALLY, like every other field on this full-replace PUT: an
            // omit-means-keep rule would make "back to unrestricted" unexpressible, since that IS
            // null. §9.1 accepted the same trade for products and closed the resulting landmine by
            // making the one writer always echo the field.
            //
            // That mitigation IS in place for bundles now: `baseMenuBundleSchema` carries
            // `availableOrderTypes` and `toBundleDefaults` seeds it, so the editor echoes what it
            // loaded. This paragraph used to say the opposite and was left behind when the
            // frontend half of §9.2 shipped.
            //
            // It is still an ECHO, not a merge — a caller that is not the admin editor and does
            // not send the field nulls the mask. Allergens below take the other approach, for a
            // reason worth contrasting.
            product.AvailableOrderTypes = command.AvailableOrderTypes;
            // `null` LEAVES THEM ALONE; `[]` clears. Those are different instructions and the
            // distinction is the whole safety property here: an older client sends nothing, and
            // must not strip a labelled combo, while an admin who unticks every chip means it.
            // The order-type mask above deliberately does NOT make that distinction — null there
            // means "inherit from the primary category", a real value.
            if (command.Allergens is not null)
            {
                product.Allergens = command.Allergens;
            }
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
            // Content may be omitted or empty — an edit that does not touch translations, say.
            // Treat that as "no translation changes" rather than NRE-ing on a null or wiping
            // every description. Mirrors UpdateProductCommandHandler, which already had both
            // guards — this handler had neither, so the same UI action meant "no-op" on a
            // product and "delete every translation" on a bundle (#190).
            var contentMap = command.Content ?? new ProductDescriptionsDto();

            // The duplicate-language-code check both handlers carried here was DEAD and has been
            // dropped: ProductDescriptionsDto derives from Dictionary<string, …>, so duplicate
            // keys cannot survive deserialization — System.Text.Json applies last-wins via the
            // indexer. Verified: a body with two "fr" entries deserializes to Count == 1, so the
            // check reported zero duplicates and its failure branch was unreachable. The copy in
            // UpdateProductCommandHandler is equally dead; removing it there belongs with that
            // handler's own tests (#193).
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

            // Update Menu Definition.
            //
            // product.MenuDefinition is safe to hand straight over because the product query above
            // loads it WITH its sections (ThenInclude) — which is what ReplaceSections below
            // requires. The product handler does not pass its navigation, because its own query
            // omits that ThenInclude; it re-queries instead. See MenuDefinitionWriter.Upsert.
            var menuDef = MenuDefinitionWriter.Upsert(
                _context,
                product.MenuDefinition,
                product.Id,
                command.MenuDefinition,
                _currentUserService.GetAuditIdentifier());

            // Update Sections — a full replace, like every other field on this PUT.
            //
            // The null-check on Sections that used to wrap this block was DEAD (#191):
            // MenuDefinitionDto.Sections carried an initializer, so an omitted key
            // deserialized to `[]` and took the wipe branch — the RemoveRange ran and the loop
            // re-added nothing. Every payload except an explicit JSON `null` (which no client
            // sends) therefore erased every section.
            //
            // Fixed at the contract instead of the branch: the property lost its initializer and
            // MenuBundleCommandValidatorBase now requires the key, so an omission is a 400. That
            // makes null unreachable here — but the throw is what keeps it unreachable SAFELY. A
            // `?? []` would silently restore the exact wipe this fixes, and a `!` would trade a
            // 400 for a 500.
            var sections = command.MenuDefinition.Sections
                ?? throw new BadRequestException(MenuDefinitionDto.SectionsRequiredMessage);

            MenuSectionWriter.ReplaceSections(_context, menuDef, sections, _currentUserService.GetAuditIdentifier());

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
                                // #468: the shared bundle mapper projects each option's recipe, and
                                // an unloaded collection is EMPTY rather than absent — the echo
                                // would state that every option has no ingredients.
                                .ThenInclude(p => p.DetailedIngredients)
                                    .ThenInclude(di => di.Descriptions)
                // Split: 2+ collection Includes over ONE root still multiply rows, and #468 adds a
                // fifth level to the chain (S8733). Behaviour is identical — it is query metadata.
                .AsSplitQuery()
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
