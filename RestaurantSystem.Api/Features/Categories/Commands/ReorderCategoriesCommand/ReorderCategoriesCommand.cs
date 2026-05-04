using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Commands.ReorderCategoriesCommand;

public record ReorderCategoriesCommand(
    List<CategoryOrderDto> CategoryOrders
) : ICommand<ApiResponse<string>>;

public class ReorderCategoriesCommandHandler : ICommandHandler<ReorderCategoriesCommand, ApiResponse<string>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ReorderCategoriesCommandHandler> _logger;

    public ReorderCategoriesCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<ReorderCategoriesCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(ReorderCategoriesCommand command, CancellationToken cancellationToken)
    {
        var duplicateOrders = command.CategoryOrders
        .GroupBy(co => co.DisplayOrder)
        .Where(g => g.Count() > 1)
        .Select(g => g.Key)
        .ToList();

        if (duplicateOrders.Any())
        {
            return ApiResponse<string>.Failure($"Duplicate display orders found: {string.Join(", ", duplicateOrders)}");
        }

        var categoryIds = command.CategoryOrders.Select(co => co.CategoryId).ToList();
        var categories = await _context.Categories
            .Where(c => categoryIds.Contains(c.Id) && !c.IsDeleted)
            .ToListAsync(cancellationToken);

        if (categories.Count != categoryIds.Count)
        {
            return ApiResponse<string>.Failure("One or more categories not found");
        }

        foreach (var categoryOrder in command.CategoryOrders)
        {
            var category = categories.First(c => c.Id == categoryOrder.CategoryId);
            category.DisplayOrder = categoryOrder.DisplayOrder;
            category.UpdatedAt = DateTime.UtcNow;
            category.UpdatedBy = _currentUserService.GetAuditIdentifier();
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Categories reordered successfully");
        return ApiResponse<string>.SuccessWithData("Categories reordered successfully");
    }
}
