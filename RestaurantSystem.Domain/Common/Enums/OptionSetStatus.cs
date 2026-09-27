using System.Runtime.Serialization;

namespace RestaurantSystem.Domain.Common.Enums;

public enum OptionSetStatus
{
    [EnumMember(Value = "active")]
    Active = 0,
    [EnumMember(Value = "archived")]
    Archived = 1
}
