namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Preparation work represented by an immutable kitchen correction.</summary>
public enum KitchenChangeKind
{
    Add = 1,
    Void = 2,
    Replace = 3,
    InstructionChange = 4
}
