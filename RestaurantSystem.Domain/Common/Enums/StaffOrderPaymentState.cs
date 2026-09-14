namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Payment state accepted when a counter order is created.</summary>
/// <remarks>Both values describe an unpaid order; recording a tender is a separate staff action.</remarks>
public enum StaffOrderPaymentState
{
    Unpaid = 1,
    PayLater = 2
}
