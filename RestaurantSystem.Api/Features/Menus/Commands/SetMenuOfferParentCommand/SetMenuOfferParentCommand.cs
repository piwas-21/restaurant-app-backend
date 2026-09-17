using System.Data;
using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<SetMenuOfferParentCommandHandler> _logger;

    public SetMenuOfferParentCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<SetMenuOfferParentCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<MenuOfferLinkDto>> Handle(
        SetMenuOfferParentCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            // The child must be loaded only after the serializable transaction begins. A stale
            // pre-transaction read could approve a normal menu while a concurrent product update
            // turns it into a component, leaving an unorderable offer-family relationship.
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
        catch (Exception exception)
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackException)
            {
                // The original exception determines the API result; disposing the transaction
                // still rolls back when an explicit rollback is unavailable.
                _logger.LogWarning(
                    rollbackException,
                    "Transaction rollback failed while updating menu offer parent for {MenuProductId}",
                    command.MenuProductId);
            }

            MenuOfferLinkConflict.ThrowIfExpected(exception);
            throw;
        }
    }
}
