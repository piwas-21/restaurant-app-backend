using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.User.Dtos;

public class UserDto
{
    /// <summary>
    /// User's unique identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// User's email address
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// User's first name
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// User's last name
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// User's full name (computed from first and last name)
    /// </summary>
    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// User's phone number
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// User's role in the system
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the user's email has been confirmed
    /// </summary>
    public bool IsEmailConfirmed { get; set; }

    /// <summary>
    /// Date and time when the user was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Date and time when the user was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Indicates whether the user is soft-deleted
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Date and time when the user was deleted
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Additional metadata associated with the user
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Order amount threshold for applying discount
    /// </summary>
    public decimal OrderLimitAmount { get; set; }

    /// <summary>
    /// Percentage discount to apply when order meets threshold
    /// </summary>
    public decimal DiscountPercentage { get; set; }

    /// <summary>
    /// Indicates whether the discount feature is active for this user
    /// </summary>
    public bool IsDiscountActive { get; set; }

}
