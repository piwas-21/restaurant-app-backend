using RestaurantSystem.Api.Features.Groups.Dtos;

namespace RestaurantSystem.Api.Features.Groups.Interfaces;

/// <summary>
/// QR-code validation and discount calculation for group memberships — the
/// money-adjacent paths extracted from <c>UserGroupService</c>. Behavior
/// (signature checks, the full validity state machine, and the best-discount
/// selection) is preserved verbatim.
/// </summary>
public interface IMembershipQrService
{
    Task<QRCodeValidationResult> ValidateMembershipByQRCodeAsync(string qrCode, CancellationToken cancellationToken = default);
    Task<decimal> CalculateDiscountAsync(Guid membershipId, decimal orderAmount, CancellationToken cancellationToken = default);
}
