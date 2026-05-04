using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UpdateProductImageCommand;

public record UpdateProductImageCommand(
    Guid ProductId,
    Guid ImageId,
    string? AltText,
    bool? IsPrimary,
    int? SortOrder
) : ICommand<ApiResponse<ProductImageDto>>;

public class UpdateProductImageCommandHandler : ICommandHandler<UpdateProductImageCommand, ApiResponse<ProductImageDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateProductImageCommandHandler> _logger;

    public UpdateProductImageCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<UpdateProductImageCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<ProductImageDto>> Handle(UpdateProductImageCommand command, CancellationToken cancellationToken)
    {
        var image = await _context.ProductImages
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.Id == command.ImageId && i.ProductId == command.ProductId && !i.IsDeleted, cancellationToken);

        if (image == null)
        {
            return ApiResponse<ProductImageDto>.Failure("Image not found");
        }

        // Update properties
        if (!string.IsNullOrWhiteSpace(command.AltText))
        {
            image.AltText = command.AltText;
        }

        if (command.SortOrder.HasValue)
        {
            image.SortOrder = command.SortOrder.Value;
        }

        // Handle primary image update
        if (command.IsPrimary.HasValue && command.IsPrimary.Value)
        {
            // Unset other primary images
            var otherPrimaryImages = await _context.ProductImages
                .Where(i => i.ProductId == command.ProductId && i.IsPrimary && i.Id != command.ImageId && !i.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var img in otherPrimaryImages)
            {
                img.IsPrimary = false;
                img.UpdatedAt = DateTime.UtcNow;
                img.UpdatedBy = _currentUserService.GetAuditIdentifier();
            }

            image.IsPrimary = true;
        }
        else if (command.IsPrimary.HasValue && !command.IsPrimary.Value)
        {
            image.IsPrimary = false;
        }

        image.UpdatedAt = DateTime.UtcNow;
        image.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        var responseDto = new ProductImageDto
        {
            Url = image.Url,
            AltText = image.AltText,
            IsPrimary = image.IsPrimary,
            SortOrder = image.SortOrder,
            ProductId = image.ProductId
        };

        _logger.LogInformation("Image {ImageId} updated for product {ProductId} by user {UserId}",
            command.ImageId, command.ProductId, _currentUserService.UserId);

        return ApiResponse<ProductImageDto>.SuccessWithData(responseDto, "Image updated successfully");
    }
}
