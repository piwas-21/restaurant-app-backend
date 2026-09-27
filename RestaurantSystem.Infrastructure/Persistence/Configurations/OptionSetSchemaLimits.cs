namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public static class OptionSetSchemaLimits
{
    public const int OptionSetNameLength = 120;
    public const int TranslationNameLength = 120;
    public const int SourceIdentifierLength = 120;
    public const int SourceLocaleLength = 10;
    public const int EntryNameLength = 200;
    public const int NormalizedNameLength = 160;
    public const int AliasLength = 160;
    public const int IdempotencyKeyLength = 100;
    public const int IntentionalDifferenceReasonLength = 500;
}
