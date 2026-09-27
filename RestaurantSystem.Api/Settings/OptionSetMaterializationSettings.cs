namespace RestaurantSystem.Api.Settings;

public sealed class OptionSetMaterializationSettings
{
    public const string SectionName = "OptionSetMaterialization";

    public int MaximumIdempotencyKeyLength { get; set; }
    public int MaximumTargetsPerRequest { get; set; }
    public int MaximumEntriesPerTarget { get; set; }
    public int MaximumIntentionalDifferenceReasonLength { get; set; }
}
