using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class CurrentStaffTicketSettings
{
    public const string SectionName = "CurrentStaffTicket";

    [Range(1, 1000)]
    public int MaximumMembers { get; set; } = 100;

    [Range(1, 10000)]
    public int MaximumAmendments { get; set; } = 1000;
}
