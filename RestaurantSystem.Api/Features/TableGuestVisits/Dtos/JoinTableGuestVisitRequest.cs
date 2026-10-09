using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

public sealed record JoinTableGuestVisitRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string QrCodeData { get; init; } = string.Empty;

    [Required, StringLength(10, MinimumLength = 6)]
    public string AdmissionCode { get; init; } = string.Empty;
}
