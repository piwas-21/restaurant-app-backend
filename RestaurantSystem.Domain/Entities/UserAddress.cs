using RestaurantSystem.Domain.Common.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RestaurantSystem.Domain.Entities;

public class UserAddress : SoftDeleteEntity
{
    public Guid UserId { get; set; }
    public string Label { get; set; } = null!; // "Home", "Work", "Other", etc.
    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = null!;
    public string? State { get; set; }
    public string PostalCode { get; set; } = null!;
    public string Country { get; set; } = null!;
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? DeliveryInstructions { get; set; }

    // Navigation property
    public virtual ApplicationUser User { get; set; } = null!;
}
