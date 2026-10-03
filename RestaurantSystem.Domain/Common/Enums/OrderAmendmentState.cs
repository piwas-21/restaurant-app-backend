namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Lifecycle of a server-priced native order amendment quote and commit.</summary>
public enum OrderAmendmentState
{
    Quoted = 1,
    Committed = 2
}

/// <summary>Requested source-line operation. New preparation lines live on a linked supplement order.</summary>
public enum OrderAmendmentChangeKind
{
    Void = 1,
    Replace = 2,
    InstructionChange = 3
}
