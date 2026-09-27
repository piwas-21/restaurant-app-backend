using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal sealed class OptionSetTargetState
{
    public required Product Product { get; init; }
    public MenuDefinition? MenuDefinition { get; init; }
    public MenuSection? Section { get; init; }
    public ProductCustomizationGroup? CustomizationGroup { get; init; }
    public OptionSetAttachment? Attachment { get; init; }
    public required List<OptionSetEntry> SelectedEntries { get; init; }
    public required Dictionary<Guid, OptionSetAppliedRow> AppliedByEntry { get; init; }
    public required OptionSetAttachmentSettings CurrentSettings { get; init; }
    public required OptionSetAttachmentSettings Settings { get; init; }
}
