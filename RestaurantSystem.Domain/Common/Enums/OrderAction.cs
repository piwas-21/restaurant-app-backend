namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Actions a staff order surface may explain for one order.</summary>
public enum OrderAction
{
    Accept,
    StartPreparing,
    MarkReady,
    HandOver,
    CollectPayment,
    AddOperationalNote,
    PrintKitchen,
    PrintReceipt,
    RefundPayment,
    CancelOrder,
    MarkUrgent
}
