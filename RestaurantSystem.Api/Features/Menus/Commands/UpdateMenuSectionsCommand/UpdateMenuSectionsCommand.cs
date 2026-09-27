using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Commands.UpdateMenuSectionsCommand;

public record UpdateMenuSectionsCommand(
    Guid MenuProductId,
    int ExpectedAuthoringVersion,
    List<MenuSectionDto>? Sections)
    : ICommand<ApiResponse<MenuSectionsPatchResultDto>>;

public sealed class UpdateMenuSectionsCommandHandler(
    ApplicationDbContext context,
    ICurrentUserService currentUser,
    ILogger<UpdateMenuSectionsCommandHandler> logger)
    : ICommandHandler<UpdateMenuSectionsCommand, ApiResponse<MenuSectionsPatchResultDto>>
{
    private readonly ApplicationDbContext _context = context;
    private readonly ICurrentUserService _currentUser = currentUser;
    private readonly ILogger<UpdateMenuSectionsCommandHandler> _logger = logger;

    public async Task<ApiResponse<MenuSectionsPatchResultDto>> Handle(
        UpdateMenuSectionsCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ExpectedAuthoringVersion <= 0)
        {
            throw new BadRequestException("A valid If-Match menu version is required");
        }

        var definition = await _context.MenuDefinitions
            .AsSplitQuery()
            .Include(menu => menu.Product)
            .Include(menu => menu.Sections)
                .ThenInclude(section => section.Translations)
            .Include(menu => menu.Sections)
                .ThenInclude(section => section.Items)
            .FirstOrDefaultAsync(menu => menu.ProductId == command.MenuProductId, cancellationToken);

        if (definition is null || definition.Product.Type != ProductType.Menu || definition.Product.IsDeleted)
        {
            throw new NotFoundException("Menu bundle not found");
        }

        if (definition.AuthoringVersion != command.ExpectedAuthoringVersion)
        {
            throw new ConflictException(MenuOfferLinkConflict.MenuAuthoringMessage);
        }

        if (command.Sections is not null)
        {
            MenuSectionWriter.ApplyPatch(
                _context,
                definition,
                command.Sections,
                _currentUser.GetAuditIdentifier());
            await MenuSectionVariationValidator.ValidateEntitiesAsync(
                _context, definition.Sections, cancellationToken);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new ConflictException(MenuOfferLinkConflict.MenuAuthoringMessage, exception);
            }

            _logger.LogInformation(
                "Updated menu sections for {MenuProductId} by {Actor}; resulting section IDs: {SectionIds}",
                command.MenuProductId,
                _currentUser.GetAuditIdentifier(),
                definition.Sections.Select(section => section.Id).ToArray());
        }

        return ApiResponse<MenuSectionsPatchResultDto>.SuccessWithData(new MenuSectionsPatchResultDto
        {
            AuthoringVersion = definition.AuthoringVersion,
            Sections = definition.Sections
                .OrderBy(section => section.DisplayOrder)
                .Select(ToDto)
                .ToList()
        });
    }

    private static MenuSectionDto ToDto(MenuSection section) => new()
    {
        Id = section.Id,
        Name = section.Name,
        Description = section.Description,
        DisplayOrder = section.DisplayOrder,
        IsRequired = section.IsRequired,
        MinSelection = section.MinSelection,
        MaxSelection = section.MaxSelection,
        Translations = MenuSectionLocale.ToDto(section.Translations),
        Items = section.Items
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new MenuSectionItemDto
            {
                Id = item.Id,
                ProductId = item.ProductId,
                ProductVariationId = item.ProductVariationId,
                AdditionalPrice = item.AdditionalPrice,
                DisplayOrder = item.DisplayOrder,
                IsDefault = item.IsDefault
            })
            .ToList()
    };
}
