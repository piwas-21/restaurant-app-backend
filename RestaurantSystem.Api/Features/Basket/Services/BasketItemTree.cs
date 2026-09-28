using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>Traverses basket rows from a flat loaded item set, including nested product choices.</summary>
internal static class BasketItemTree
{
    public static List<BasketItem> WithDescendants(
        BasketItem root,
        IEnumerable<BasketItem> allItems)
    {
        var childrenByParent = allItems
            .Where(item => item.ParentBasketItemId.HasValue)
            .GroupBy(item => item.ParentBasketItemId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var result = new List<BasketItem> { root };
        var visited = new HashSet<Guid> { root.Id };

        void Visit(Guid parentId)
        {
            if (!childrenByParent.TryGetValue(parentId, out var children))
            {
                return;
            }

            foreach (var child in children)
            {
                if (!visited.Add(child.Id))
                {
                    continue;
                }

                result.Add(child);
                Visit(child.Id);
            }
        }

        Visit(root.Id);
        return result;
    }

    public static List<BasketItem> Descendants(
        BasketItem root,
        IEnumerable<BasketItem> allItems) =>
        WithDescendants(root, allItems).Skip(1).ToList();
}
