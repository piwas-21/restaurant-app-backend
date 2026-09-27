using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class OptionSetTranslation : Entity
{
    public Guid OptionSetId { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public virtual OptionSet OptionSet { get; set; } = null!;
}
