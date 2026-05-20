using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UnsetFeaturedSpecialCommand;

/// <summary>
/// Command to remove the currently featured special
/// </summary>
public record UnsetFeaturedSpecialCommand : ICommand<ApiResponse<string>>;

public class UnsetFeaturedSpecialCommandHandler : ICommandHandler<UnsetFeaturedSpecialCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UnsetFeaturedSpecialCommandHandler> _logger;

    public UnsetFeaturedSpecialCommandHandler(
        ApplicationDbContext context,
        ILogger<UnsetFeaturedSpecialCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(
        UnsetFeaturedSpecialCommand command,
        CancellationToken cancellationToken)
    {
        // Find the currently featured special
        var featuredProduct = await _context.Products
            .FirstOrDefaultAsync(p => p.IsFeaturedSpecial, cancellationToken);

        if (featuredProduct == null)
        {
            _logger.LogInformation("No featured special to unset");
            return ApiResponse<string>.SuccessWithData(
                string.Empty,
                "No featured special was set");
        }

        // Unset the featured special
        var productName = featuredProduct.Name;
        var productId = featuredProduct.Id;

        featuredProduct.IsFeaturedSpecial = false;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Removed featured special: {ProductName} (ID: {ProductId})",
            productName, productId);

        return ApiResponse<string>.SuccessWithData(
            string.Empty,
            $"Successfully removed '{productName}' as featured special");
    }
}
