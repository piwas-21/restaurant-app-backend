using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class OptionSet : Entity
{
    public OptionSetKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceLocale { get; set; } = "en";
    public string NormalizedName { get; set; } = string.Empty;
    public OptionSetStatus Status { get; set; } = OptionSetStatus.Active;
    public int Version { get; set; } = 1;
    public string? SourceTemplateId { get; set; }
    public int? SourceRevision { get; set; }
    public string? SourceOptionSetId { get; set; }
    public virtual ICollection<OptionSetEntry> Entries { get; set; } = [];
    public virtual ICollection<OptionSetAttachment> Attachments { get; set; } = [];
    public virtual ICollection<OptionSetTranslation> Translations { get; set; } = [];
}
