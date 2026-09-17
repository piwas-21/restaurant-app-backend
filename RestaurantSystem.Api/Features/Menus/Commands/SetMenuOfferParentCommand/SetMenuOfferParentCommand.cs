using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Commands.SetMenuOfferParentCommand;

/// <summary>Links a menu product to an anchor, or clears the relation when both parent ids are null.</summary>
public record SetMenuOfferParentCommand(
    Guid MenuProductId,
    Guid? ParentOfferProductId,
    Guid? ParentOfferVariationId = null)
    : ICommand<ApiResponse<MenuOfferLinkDto>>;

public sealed class SetMenuOfferParentCommandHandler
    : ICommandHandler<SetMenuOfferParentCommand, ApiResponse<MenuOfferLinkDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public SetMenuOfferParentCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<MenuOfferLinkDto>> Handle(
        SetMenuOfferParentCommand command,
        CancellationToken cancellationToken)
    {
        var menu = await _context.Products
            .Include(product => product.MenuDefinition)
            .FirstOrDefaultAsync(product => product.Id == command.MenuProductId && !product.IsDeleted,
                cancellationToken);

        if (menu is null)
        {
            return ApiResponse<MenuOfferLinkDto>.Failure("Menu bundle not found");
        }

        if (menu.Type != ProductType.Menu || menu.MenuDefinition is null)
        {
            return ApiResponse<MenuOfferLinkDto>.Failure("Product is not a menu bundle");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await MenuOfferLinkRules.EnsureValidAsync(
                _context,
                menu.Id,
                command.ParentOfferProductId,
                command.ParentOfferVariationId,
                cancellationToken);

            menu.MenuDefinition.ParentOfferProductId = command.ParentOfferProductId;
            menu.MenuDefinition.ParentOfferVariationId = command.ParentOfferVariationId;
            menu.MenuDefinition.UpdatedAt = DateTime.UtcNow;
            menu.MenuDefinition.UpdatedBy = _currentUserService.GetAuditIdentifier();

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var link = new MenuOfferLinkDto(
                menu.Id,
                menu.MenuDefinition.ParentOfferProductId,
                menu.MenuDefinition.ParentOfferVariationId);
            return ApiResponse<MenuOfferLinkDto>.SuccessWithData(link);
        }
        catch (DbUpdateException exception) when (IsOfferLinkConflict(exception))
        {
            throw new BadRequestException(
                "This parent offer already has a menu alternative for that variation");
        }
    }

    private static bool IsOfferLinkConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_menu_definitions_parent_offer_product_id"
                or "ux_menu_definitions_parent_offer_variation"
        };
}
