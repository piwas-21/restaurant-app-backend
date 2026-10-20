namespace RestaurantSystem.Api.Settings;

/// <summary>Shared-app provider audiences; absent configuration exposes no native social flow.</summary>
public sealed class CustomerNativeSocialSettings
{
    public const string SectionName = "CustomerNativeSocial";
    public bool GoogleEnabled { get; set; }
    public string[] GoogleClientIds { get; set; } = [];
    public int ChallengeLifetimeSeconds { get; set; } = 180;

    public bool IsValid() => !GoogleEnabled || ChallengeLifetimeSeconds is >= 60 and <= 300
        && GoogleClientIds is { Length: >= 1 and <= 10 }
        && GoogleClientIds.Distinct(StringComparer.Ordinal).Count() == GoogleClientIds.Length
        && GoogleClientIds.All(id => id is { Length: >= 1 and <= 256 }
            && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
}
