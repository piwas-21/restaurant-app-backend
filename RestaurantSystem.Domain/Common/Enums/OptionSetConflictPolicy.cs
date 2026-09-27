using System.Runtime.Serialization;

namespace RestaurantSystem.Domain.Common.Enums;

public enum OptionSetConflictPolicy
{
    [EnumMember(Value = "preserveLocal")]
    PreserveLocal = 0,
    [EnumMember(Value = "useSetValues")]
    UseSetValues = 1,
    [EnumMember(Value = "useOverrides")]
    UseOverrides = 2
}
