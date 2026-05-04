using RestaurantSystem.Domain.Common.Interfaces;
namespace RestaurantSystem.Domain.Common.Base
{
    public abstract class BaseEntity : IAuditable
    {
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public required string CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
