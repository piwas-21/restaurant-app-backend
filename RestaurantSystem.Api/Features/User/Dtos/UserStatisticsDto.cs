namespace RestaurantSystem.Api.Features.User.Dtos;

/// <summary>
/// User statistics data transfer object
/// </summary>
public class UserStatisticsDto
{
    /// <summary>
    /// Total number of customers (excluding deleted)
    /// </summary>
    public int TotalCustomers { get; set; }

    /// <summary>
    /// Total number of staff members (excluding deleted)
    /// </summary>
    public int TotalStaff { get; set; }

    /// <summary>
    /// Total number of administrators (excluding deleted)
    /// </summary>
    public int TotalAdmins { get; set; }

    /// <summary>
    /// Total number of deleted users
    /// </summary>
    public int DeletedUsers { get; set; }

    /// <summary>
    /// Number of users registered in the last 7 days
    /// </summary>
    public int RecentRegistrations { get; set; }

    /// <summary>
    /// Number of users with active discount settings
    /// </summary>
    public int ActiveDiscounts { get; set; }
}
