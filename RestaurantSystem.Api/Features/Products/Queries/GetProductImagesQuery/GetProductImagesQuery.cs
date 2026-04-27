using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductImagesQuery;

public record GetProductImagesQuery(Guid ProductId) : IQuery<ApiResponse<List<ProductImageDto>>>;

public class GetProductImagesQueryHandler : IQueryHandler<GetProductImagesQuery, ApiResponse<List<ProductImageDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetProductImagesQueryHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _baseUrl;

    public GetProductImagesQueryHandler(ApplicationDbContext context, ILogger<GetProductImagesQueryHandler> logger, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
        _configuration = configuration;
    }

    public async Task<ApiResponse<List<ProductImageDto>>> Handle(GetProductImagesQuery query, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .Include(p => p.Images.Where(i => !i.IsDeleted).OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(p => p.Id == query.ProductId && !p.IsDeleted, cancellationToken);

        if (product == null)
        {
            return ApiResponse<List<ProductImageDto>>.Failure("Product not found");
        }

        var images = product.Images.Select(i => new ProductImageDto
        {
            Id = i.Id,
            Url = _baseUrl + "/" + i.Url,
            AltText = i.AltText,
            IsPrimary = i.IsPrimary,
            SortOrder = i.SortOrder,
            ProductId = i.ProductId
        }).ToList();

        _logger.LogInformation("Retrieved {Count} images for product {ProductId}", images.Count, query.ProductId);

        return ApiResponse<List<ProductImageDto>>.SuccessWithData(images);
    }
}
