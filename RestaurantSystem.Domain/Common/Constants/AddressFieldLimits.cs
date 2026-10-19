namespace RestaurantSystem.Domain.Common.Constants;

/// <summary>Unicode character bounds in the user-address storage schema.</summary>
public static class AddressFieldLimits
{
    public const int Label = 50;
    public const int Line = 200;
    public const int City = 100;
    public const int State = 100;
    public const int PostalCode = 20;
    public const int Country = 100;
    public const int Phone = 20;
    public const int Instructions = 500;
}
