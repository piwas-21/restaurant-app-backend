using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Commands.DeleteMenuBundleCommand;

public record DeleteMenuBundleCommand(Guid Id) : ICommand<ApiResponse<string>>;

public class DeleteMenuBundleCommandHandler : ICommandHandler<DeleteMenuBundleCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteMenuBundleCommandHandler> _logger;

    public DeleteMenuBundleCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<DeleteMenuBundleCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(DeleteMenuBundleCommand command, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(c => c.Id == command.Id && !c.IsDeleted, cancellationToken);

        if (product == null)
        {
            return ApiResponse<string>.Failure("Menu bundle not found");
        }

        if (product.Type != ProductType.Menu)
        {
            return ApiResponse<string>.Failure("Product is not a menu bundle");
        }

        // Soft delete
        product.IsDeleted = true;
        product.DeletedAt = DateTime.UtcNow;
        product.DeletedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Menu Bundle {ProductId} deleted successfully", product.Id);
        return ApiResponse<string>.SuccessWithData("Menu bundle deleted successfully");
    }
}
