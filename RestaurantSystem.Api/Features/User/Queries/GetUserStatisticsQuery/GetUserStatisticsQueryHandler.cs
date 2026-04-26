using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.User.Queries.GetUserStatisticsQuery;

public class GetUserStatisticsQueryHandler : IQueryHandler<GetUserStatisticsQuery, ApiResponse<UserStatisticsDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetUserStatisticsQueryHandler> _logger;

    public GetUserStatisticsQueryHandler(ApplicationDbContext context, ILogger<GetUserStatisticsQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<UserStatisticsDto>> Handle(GetUserStatisticsQuery query, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting to retrieve user statistics");

            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

            _logger.LogInformation("Counting customers...");
            var totalCustomers = await _context.Users
                .Where(u => u.Role == UserRole.Customer && !u.IsDeleted)
                .CountAsync(cancellationToken);
            _logger.LogInformation("Total customers: {Count}", totalCustomers);

            _logger.LogInformation("Counting staff...");
            var totalStaff = await _context.Users
                .Where(u => (u.Role == UserRole.Cashier || u.Role == UserRole.KitchenStaff || u.Role == UserRole.Server) && !u.IsDeleted)
                .CountAsync(cancellationToken);
            _logger.LogInformation("Total staff: {Count}", totalStaff);

            _logger.LogInformation("Counting admins...");
            var totalAdmins = await _context.Users
                .Where(u => u.Role == UserRole.Admin && !u.IsDeleted)
                .CountAsync(cancellationToken);
            _logger.LogInformation("Total admins: {Count}", totalAdmins);

            _logger.LogInformation("Counting deleted users...");
            var deletedUsers = await _context.Users
                .Where(u => u.IsDeleted)
                .CountAsync(cancellationToken);
            _logger.LogInformation("Total deleted users: {Count}", deletedUsers);

            _logger.LogInformation("Counting recent registrations...");
            var recentRegistrations = await _context.Users
                .Where(u => u.CreatedAt >= sevenDaysAgo && !u.IsDeleted)
                .CountAsync(cancellationToken);
            _logger.LogInformation("Recent registrations: {Count}", recentRegistrations);

            _logger.LogInformation("Counting active discounts...");
            var activeDiscounts = await _context.Users
                .Where(u => u.IsDiscountActive && !u.IsDeleted)
                .CountAsync(cancellationToken);
            _logger.LogInformation("Active discounts: {Count}", activeDiscounts);

            var statistics = new UserStatisticsDto
            {
                TotalCustomers = totalCustomers,
                TotalStaff = totalStaff,
                TotalAdmins = totalAdmins,
                DeletedUsers = deletedUsers,
                RecentRegistrations = recentRegistrations,
                ActiveDiscounts = activeDiscounts
            };

            _logger.LogInformation(
                "User statistics: {Customers} customers, {Staff} staff, {Admins} admins, " +
                "{Deleted} deleted, {Recent} recent, {Discounts} active discounts",
                statistics.TotalCustomers,
                statistics.TotalStaff,
                statistics.TotalAdmins,
                statistics.DeletedUsers,
                statistics.RecentRegistrations,
                statistics.ActiveDiscounts
            );

            return ApiResponse<UserStatisticsDto>.SuccessWithData(statistics, "User statistics retrieved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user statistics. Message: {Message}, StackTrace: {StackTrace}",
                ex.Message, ex.StackTrace);
            return ApiResponse<UserStatisticsDto>.Failure($"Failed to retrieve user statistics: {ex.Message}");
        }
    }
}
