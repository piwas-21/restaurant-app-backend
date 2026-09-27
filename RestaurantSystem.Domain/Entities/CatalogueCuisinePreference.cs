using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>One tenant-local cuisine preference row used only to rank catalogue discovery.</summary>
public sealed class CatalogueCuisinePreference : Entity
{
    public static readonly Guid SingletonId = new("4a44b885-29e2-4000-8000-000000000006");

    public List<string> Cuisines { get; set; } = [];
}
