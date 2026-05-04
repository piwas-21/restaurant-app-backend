using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.DeleteProductImageCommand;

public record DeleteProductImageCommand(Guid ProductId, Guid ImageId) : ICommand<ApiResponse<string>>;

public class DeleteProductImageCommandHandler : ICommandHandler<DeleteProductImageCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteProductImageCommandHandler> _logger;

    public DeleteProductImageCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        ILogger<DeleteProductImageCommandHandler> logger)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(DeleteProductImageCommand command, CancellationToken cancellationToken)
    {
        var image = await _context.ProductImages
            .FirstOrDefaultAsync(i => i.Id == command.ImageId && i.ProductId == command.ProductId && !i.IsDeleted, cancellationToken);

        if (image == null)
        {
            return ApiResponse<string>.Failure("Image not found");
        }

        // Soft delete the image
        image.IsDeleted = true;
        image.DeletedAt = DateTime.UtcNow;
        image.DeletedBy = _currentUserService.GetAuditIdentifier();

        // If this was the primary image, set another as primary
        if (image.IsPrimary)
        {
            var nextImage = await _context.ProductImages
                .Where(i => i.ProductId == command.ProductId && i.Id != command.ImageId && !i.IsDeleted)
                .OrderBy(i => i.SortOrder)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextImage != null)
            {
                nextImage.IsPrimary = true;
                nextImage.UpdatedAt = DateTime.UtcNow;
                nextImage.UpdatedBy = _currentUserService.GetAuditIdentifier();
            }
        }

        // Optionally delete from storage (you might want to keep it for recovery)
        try
        {
            await _fileStorageService.DeleteFileAsync(image.Url, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete image file from storage: {Url}", image.Url);
            // Continue with soft delete even if storage deletion fails
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Image {ImageId} deleted for product {ProductId} by user {UserId}",
            command.ImageId, command.ProductId, _currentUserService.UserId);

        return ApiResponse<string>.SuccessWithData("Image deleted successfully");
    }
}
