using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UpdateProductPriceCommand;

/// <summary>
/// Admin quick-edit: update ONLY a product's base price. A deliberately minimal,
/// single-field write path (used by the inline price edit on the menu cards) so a
/// price tweak does not have to round-trip the full UpdateProductCommand.
/// </summary>
public record UpdateProductPriceCommand(Guid Id, decimal Price) : ICommand<ApiResponse<decimal>>;

public class UpdateProductPriceCommandHandler
    : ICommandHandler<UpdateProductPriceCommand, ApiResponse<decimal>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateProductPriceCommandHandler> _logger;

    public UpdateProductPriceCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<UpdateProductPriceCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<decimal>> Handle(
        UpdateProductPriceCommand command,
        CancellationToken cancellationToken)
    {
        // The soft-delete global filter means a deleted product resolves to null → 404.
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException("Product", command.Id);

        product.BasePrice = command.Price;
        // The async SaveChanges override does not run ApplyAuditInformation in this codebase, so
        // set the audit fields explicitly (mirrors UpdateProductCommand). A price change is a
        // money-mutating admin action and must stay traceable.
        product.UpdatedAt = DateTime.UtcNow;
        product.UpdatedBy = _currentUserService.GetAuditIdentifier();
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated base price for product {ProductId} to {Price}", product.Id, command.Price);

        return ApiResponse<decimal>.SuccessWithData(product.BasePrice, "Price updated");
    }
}
