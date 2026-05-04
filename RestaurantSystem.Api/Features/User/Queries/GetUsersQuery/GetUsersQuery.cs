using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Queries.GetUsersQuery;

public record GetUsersQuery(
    UserRole? Role,
    bool? IsDeleted,
    string? Search,
    int Page = 1,
    int PageSize = 20
) : IQuery<ApiResponse<PagedResult<UserDto>>>;

public class GetProductsQueryHandler : IQueryHandler<GetUsersQuery, ApiResponse<PagedResult<UserDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetProductsQueryHandler> _logger;

    public GetProductsQueryHandler(ApplicationDbContext context, ILogger<GetProductsQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<UserDto>>> Handle(GetUsersQuery query, CancellationToken cancellationToken)
    {
        var userQuery = _context.Users.IgnoreQueryFilters().AsQueryable();

        if (query.Role != null)
        {
            userQuery = userQuery.Where(u => u.Role == query.Role);
        }

        if (query.IsDeleted != null)
        {
            userQuery = userQuery.Where(u => u.IsDeleted == query.IsDeleted);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchLower = query.Search.ToLower();
            userQuery = userQuery.Where(u =>
                u.FirstName.ToLower().Contains(searchLower) ||
                u.LastName.ToLower().Contains(searchLower) ||
                (u.Email ?? string.Empty).ToLower().Contains(searchLower) ||
                (u.FirstName + " " + u.LastName).ToLower().Contains(searchLower));
        }

        // Get total count
        var totalCount = await userQuery.CountAsync(cancellationToken);

        // Order and paginate
        var users = await userQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FirstName = u.FirstName,
                LastName = u.LastName,
                PhoneNumber = u.PhoneNumber,
                Role = u.Role.ToString(),
                IsEmailConfirmed = u.EmailConfirmed,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
                IsDeleted = u.IsDeleted,
                DeletedAt = u.DeletedAt,
                Metadata = u.Metadata ?? new Dictionary<string, string>(),
                OrderLimitAmount = u.OrderLimitAmount,
                DiscountPercentage = u.DiscountPercentage,
                IsDiscountActive = u.IsDiscountActive
            }).ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var result = new PagedResult<UserDto>(
                users,
                totalCount,
                query.Page,
                query.PageSize,
                totalPages
            );

        _logger.LogInformation("Retrieved {UserCount} users (page {Page} of {TotalPages})",
            users.Count, query.Page, totalPages);

        return ApiResponse<PagedResult<UserDto>>.SuccessWithData(result,
            $"Retrieved {users.Count} users");
    }
}
