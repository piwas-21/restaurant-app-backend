namespace RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

/// <summary>
/// What <c>POST /api/global-ingredients/{id}/apply-translations</c> did, itemised — the receipt
/// for a write whose whole point is that its effects are spread over many products the admin has
/// not opened. The same shape of answer <see cref="AttachGlobalIngredientResultDto"/> gives for the
/// bulk attach: counts plus a per-copy list, so a client can show what changed without a second
/// request.
/// </summary>
public class ApplyGlobalIngredientTranslationsResultDto
{
    /// <summary>Distinct LIVE products that carry at least one copy the call touched.</summary>
    public int UpdatedProductCount { get; set; }

    /// <summary>Total ingredient copies touched — one product carrying two copies counts twice.</summary>
    public int UpdatedIngredientCount { get; set; }

    /// <summary>One item per copy, in no guaranteed order.</summary>
    public List<AppliedGlobalIngredientTranslationItemDto> Items { get; set; } = [];
}

/// <summary>One touched copy: WHERE it lives and WHICH row it is.</summary>
public record AppliedGlobalIngredientTranslationItemDto(
    Guid ProductId,
    string ProductName,
    Guid IngredientId);
