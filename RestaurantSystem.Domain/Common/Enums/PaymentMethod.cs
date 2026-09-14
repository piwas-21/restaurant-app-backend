namespace RestaurantSystem.Domain.Common.Enums;

public enum PaymentMethod
{
    Cash = 1,
    // Card accepted at the restaurant's till. OnlinePayment is the separate Stripe path.
    CreditCard = 2,
    DebitCard = 3,
    OnlinePayment = 4,
    MobilePayment = 5,
    BankTransfer = 6
}
