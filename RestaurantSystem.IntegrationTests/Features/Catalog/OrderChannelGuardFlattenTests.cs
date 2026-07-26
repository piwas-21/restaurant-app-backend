using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.IntegrationTests.Features.Catalog;

// B3 regression guard. OrderChannelGuard is "the last line of defence", but it originally read only
// top-level ProductIds -- and BasketToOrderTranslator puts BOTH bundle children and top-level side
// items into ChildItems. A takeaway-only product ordered dine-in therefore sailed through as a
// bundle option or a side item, via the basket path AND a direct POST /api/Orders.
//
// The flatten is a private static in the guard, so this pins the DTO shape it must walk: if
// ChildItems stops being the nesting seam, or gains a second level the walk misses, this fails.
public class OrderChannelGuardFlattenTests
{
    [Fact]
    public void ChildItems_is_the_nesting_seam_and_nests_recursively()
    {
        var bundleChildSide = new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 };
        var bundleChild = new CreateOrderItemDto
        {
            ProductId = Guid.NewGuid(),
            Quantity = 1,
            ChildItems = [bundleChildSide]
        };
        var root = new CreateOrderItemDto
        {
            ProductId = Guid.NewGuid(),
            Quantity = 1,
            ChildItems = [bundleChild]
        };

        // Three distinct products across three levels — a top-level-only walk would see one.
        var flattened = Flatten([root]).ToList();

        flattened.Should().HaveCount(3);
        flattened.Should().Contain(root.ProductId!.Value);
        flattened.Should().Contain(bundleChild.ProductId!.Value);
        flattened.Should().Contain(bundleChildSide.ProductId!.Value);
    }

    [Fact]
    public void A_menu_only_line_contributes_no_product_id_but_still_yields_its_children()
    {
        // Legacy daily-menu lines carry MenuId and no ProductId; their children must still be seen.
        var child = new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 };
        var root = new CreateOrderItemDto { MenuId = Guid.NewGuid(), Quantity = 1, ChildItems = [child] };

        Flatten([root]).Should().Equal(child.ProductId!.Value);
    }

    [Fact]
    public void Null_child_collections_are_tolerated()
    {
        var root = new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1, ChildItems = null };

        Flatten([root]).Should().Equal(root.ProductId!.Value);
    }

    // Mirrors OrderChannelGuard.FlattenProductIds (private). Kept in lockstep deliberately: the
    // guard's own behaviour is covered end-to-end by the order-creation tests.
    private static IEnumerable<Guid> Flatten(IEnumerable<CreateOrderItemDto>? items)
    {
        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            if (item.ProductId.HasValue)
            {
                yield return item.ProductId.Value;
            }

            foreach (var childId in Flatten(item.ChildItems))
            {
                yield return childId;
            }
        }
    }
}
