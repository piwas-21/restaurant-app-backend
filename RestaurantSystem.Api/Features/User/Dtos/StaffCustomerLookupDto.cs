namespace RestaurantSystem.Api.Features.User.Dtos;

/// <summary>Minimal customer identity and server-owned loyalty balance for staff lookup.</summary>
public sealed class StaffCustomerLookupDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public int CurrentPoints { get; init; }
}
