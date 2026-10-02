namespace RestaurantSystem.Api.Settings;

public sealed class TableGuestVisitSettings
{
    public const string SectionName = "TableGuestVisits";

    public int JoinsPerMinute { get; set; } = 5;
    public int AccountReadsPerMinute { get; set; } = 60;
    public int AccountReadsPerIpPerMinute { get; set; } = 600;
    public int RoundAttemptsPerIpPerMinute { get; set; } = 100;
    public int RoundAttemptsPerMinute { get; set; } = 10;
    public int AdmissionLifetimeHours { get; set; } = 12;
    public int ParticipantLifetimeHours { get; set; } = 12;

    public bool IsValid() => JoinsPerMinute is >= 1 and <= 60
        && AccountReadsPerMinute is >= 1 and <= 600
        && RoundAttemptsPerMinute is >= 1 and <= 60
        && AccountReadsPerIpPerMinute is >= 1 and <= 6000
        && RoundAttemptsPerIpPerMinute is >= 1 and <= 1000
        && AdmissionLifetimeHours is >= 1 and <= 24
        && ParticipantLifetimeHours is >= 1 and <= 24;
}
