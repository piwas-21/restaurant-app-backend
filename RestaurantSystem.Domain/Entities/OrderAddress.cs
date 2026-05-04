using RestaurantSystem.Domain.Common.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RestaurantSystem.Domain.Entities;

public class OrderAddress : Entity
{
    public Guid OrderId { get; set; }
    public Guid? UserAddressId { get; set; } // Reference to UserAddress if it was a saved address

    // Address Details (stored independently from UserAddress for historical accuracy)
    public string Label { get; set; } = null!;
    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = null!;
    public string? State { get; set; }
    public string PostalCode { get; set; } = null!;
    public string Country { get; set; } = null!;
    public string? Phone { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? DeliveryInstructions { get; set; }

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
    public virtual UserAddress? UserAddress { get; set; } // Optional reference to original address

    // Helper method to format full address
    public string GetFullAddress()
    {
        var parts = new List<string> { AddressLine1 };

        if (!string.IsNullOrWhiteSpace(AddressLine2))
            parts.Add(AddressLine2);

        parts.Add(City);

        if (!string.IsNullOrWhiteSpace(State))
            parts.Add(State);

        parts.Add(PostalCode);
        parts.Add(Country);

        return string.Join(", ", parts);
    }
}
