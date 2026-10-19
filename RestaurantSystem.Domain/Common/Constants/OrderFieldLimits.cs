namespace RestaurantSystem.Domain.Common.Constants;

/// <summary>Storage limits shared by order persistence and request validation.</summary>
public static class OrderFieldLimits
{
    public const int CustomerNameMaxLength = 100;
    public const int CustomerEmailMaxLength = 100;
    public const int CustomerPhoneMaxLength = 20;
    public const int NotesMaxLength = 1000;
    // Order money is persisted as decimal(10,2).
    public const decimal MaximumMoneyAmount = 99_999_999.99m;
}
