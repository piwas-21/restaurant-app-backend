using RestaurantSystem.Domain.Common.Interfaces;
namespace RestaurantSystem.Domain.Common.Base
{
    public abstract class SoftDeleteEntity : Entity, ISoftDelete
    {
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }
}
