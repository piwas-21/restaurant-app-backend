using RestaurantSystem.Domain.Common.Enums;
using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>
/// An admin edit: every field a booking carries (<see cref="ReservationWriteDto"/>) plus the two
/// only an edit can set.
/// </summary>
public record UpdateReservationDto : ReservationWriteDto
{
    [Required]
    public ReservationStatus Status { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}
