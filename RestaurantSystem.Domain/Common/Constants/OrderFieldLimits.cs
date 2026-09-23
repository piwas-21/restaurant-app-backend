namespace RestaurantSystem.Domain.Common.Constants;

/// <summary>Storage limits shared by order persistence and request validation.</summary>
public static class OrderFieldLimits
{
    public const int CustomerNameMaxLength = 100;
    public const int CustomerEmailMaxLength = 100;
    public const int CustomerPhoneMaxLength = 20;
    public const int NotesMaxLength = 1000;
}
