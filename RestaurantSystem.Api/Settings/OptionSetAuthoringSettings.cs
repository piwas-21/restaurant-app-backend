namespace RestaurantSystem.Api.Settings;

public sealed class OptionSetAuthoringSettings
{
    public const string SectionName = "OptionSetAuthoring";
    public const int MaximumSynchronousTargetsLimit = 100;
    public const int MaximumJobTargetsLimit = 1000;
    public const int MaximumTargetsPerJobRunLimit = 100;
    public const int MaximumJobLeaseSecondsLimit = 3600;
    public const int MaximumJobPollIntervalSecondsLimit = 300;
    public const string ImportedSourceLabelSeparator = " · ";
    public const string ImportedSourceFingerprintSeparator = "-";

    public int MaximumIdempotencyKeyLength { get; set; }
    public int MaximumTargetsPerRequest { get; set; }
    public int MaximumTargetsPerJob { get; set; } = MaximumJobTargetsLimit;
    public int TargetsPerJobRun { get; set; } = 25;
    public int JobLeaseSeconds { get; set; } = 300;
    public int JobPollIntervalSeconds { get; set; } = 5;
    public int MaximumTargetKeyLength { get; set; }
    public int MaximumEntriesPerOptionSet { get; set; }
    public int MaximumEntryNameLength { get; set; }
    public int MaximumOptionSetNameLength { get; set; }
    public int MaximumTranslationNameLength { get; set; }
    public int MaximumIntentionalDifferenceReasonLength { get; set; }
    public int MaximumTranslationLocales { get; set; }
    public int MaximumLocaleTagLength { get; set; }
    public int MaximumSourceIdentifierLength { get; set; }
    public int MaximumImportedSourceLabelLength { get; set; }
    public int MaximumImportedSourceFingerprintLength { get; set; }
}
