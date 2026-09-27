namespace RestaurantSystem.Api.Settings;

public sealed class OptionSetAuthoringSettings
{
    public const string SectionName = "OptionSetAuthoring";

    public int MaximumIdempotencyKeyLength { get; set; }
    public int MaximumTargetsPerRequest { get; set; }
    public int MaximumEntriesPerOptionSet { get; set; }
    public int MaximumIntentionalDifferenceReasonLength { get; set; }
    public int MaximumTranslationLocales { get; set; }
    public int MaximumLocaleTagLength { get; set; }
}
