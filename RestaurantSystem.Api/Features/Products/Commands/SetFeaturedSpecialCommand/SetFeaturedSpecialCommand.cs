using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.SetFeaturedSpecialCommand;

/// <summary>
/// Command to set a product as the featured special
/// Only one product can be featured at a time
/// </summary>
public record SetFeaturedSpecialCommand(Guid ProductId) : ICommand<ApiResponse<string>>;

public class SetFeaturedSpecialCommandHandler : ICommandHandler<SetFeaturedSpecialCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SetFeaturedSpecialCommandHandler> _logger;

    public SetFeaturedSpecialCommandHandler(
        ApplicationDbContext context,
        ILogger<SetFeaturedSpecialCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(
        SetFeaturedSpecialCommand command,
        CancellationToken cancellationToken)
    {
        // Find the product
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken);

        if (product == null)
        {
            _logger.LogWarning("Product with ID {ProductId} not found", command.ProductId);
            return ApiResponse<string>.Failure("Product not found");
        }

        // Validate that the product is marked as special
        if (!product.IsSpecial)
        {
            _logger.LogWarning(
                "Cannot feature product {ProductId} - product is not marked as special",
                command.ProductId);
            return ApiResponse<string>.Failure(
                "Cannot feature this product. Only products marked as special can be featured.");
        }

        // Validate that the product is active
        if (!product.IsActive)
        {
            _logger.LogWarning(
                "Cannot feature product {ProductId} - product is not active",
                command.ProductId);
            return ApiResponse<string>.Failure(
                "Cannot feature an inactive product");
        }

        // Unset any existing featured special
        var currentFeatured = await _context.Products
            .Where(p => p.IsFeaturedSpecial)
            .ToListAsync(cancellationToken);

        foreach (var featuredProduct in currentFeatured)
        {
            featuredProduct.IsFeaturedSpecial = false;
            _logger.LogInformation(
                "Unfeaturing previous special: {ProductName} (ID: {ProductId})",
                featuredProduct.Name, featuredProduct.Id);
        }

        // Set the new featured special
        product.IsFeaturedSpecial = true;
        product.FeaturedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Set product as featured special: {ProductName} (ID: {ProductId})",
            product.Name, product.Id);

        return ApiResponse<string>.SuccessWithData(
            product.Id.ToString(),
            $"Successfully set '{product.Name}' as the featured special");
    }
}
