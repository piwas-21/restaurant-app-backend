using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class OptionSetAppliedRow : Entity
{
    public Guid OptionSetAttachmentId { get; set; }
    public Guid OptionSetEntryId { get; set; }
    public string RowType { get; set; } = string.Empty;
    public Guid MaterializedRowId { get; set; }
    public bool OwnsMaterializedRow { get; set; }
    public string LastAppliedValuesJson { get; set; } = "{}";
    public virtual OptionSetAttachment Attachment { get; set; } = null!;
    public virtual OptionSetEntry Entry { get; set; } = null!;
}
