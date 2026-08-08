namespace RestaurantSystem.Api.Settings;

public class BasketSettings
{
    public int SessionExpiryDays { get; set; } = 7;
    public int CacheExpiryMinutes { get; set; } = 30;
    public int MaxItemsPerBasket { get; set; } = 100;
    public int MaxQuantityPerItem { get; set; } = 100;
    public bool EnableAutoMergeOnLogin { get; set; } = true;
    public decimal TaxRate { get; set; } = 0.08m;
}
