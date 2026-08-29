using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// The DECISION half of "a component may only be chosen inside a bundle" (kebabdilhan G5).
/// Pure unit test — no DB. The WIRING half is <see cref="BasketComponentProductTests"/>, and both
/// are needed: a guard that is never called throws nothing and looks perfectly correct here.
/// </summary>
public class BasketComponentGuardTests
{
    private const string Actor = "component-guard-tests";

    private static Product Product(bool isComponent) =>
        new() { Name = "Poulet", IsComponent = isComponent, CreatedBy = Actor };

    [Fact]
    public void Refuses_a_component_ordered_on_its_own()
    {
        var act = () => BasketComponentGuard.EnsureNotOrderedAlone(Product(isComponent: true));

        act.Should()
            .Throw<BadRequestException>()
            .WithMessage("Poulet can only be chosen inside a menu, not ordered on its own.");
    }

    [Fact]
    public void Tags_the_rejection_with_a_code_so_the_client_can_offer_the_bundle_instead()
    {
        // The client never renders the card, so reaching this means a stale tab, the waiter screen
        // or a crafted payload — and the recovery ("order the menu") differs from every other 400
        // on the endpoint, which is why substring-matching English prose is not good enough.
        var act = () => BasketComponentGuard.EnsureNotOrderedAlone(Product(isComponent: true));

        act.Should().Throw<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ComponentNotOrderable);
    }

    [Fact]
    public void Permits_every_ordinary_catalogue_item()
    {
        // The default, and therefore the whole existing catalogue.
        var act = () => BasketComponentGuard.EnsureNotOrderedAlone(Product(isComponent: false));

        act.Should().NotThrow();
    }

    /// <summary>
    /// The flag is read RAW, with no "…and it is referenced by a bundle" and no variation clause.
    /// That is the deliberate difference from <c>HideBaseProduct</c>, whose effective rule
    /// (<c>BaseProductVisibility.IsBaseHidden</c>) degrades to <c>false</c> when no variation is
    /// active — precisely the shape a component has, so reusing that flag would have produced a
    /// rule that silently does nothing for the products this feature exists for.
    /// </summary>
    [Fact]
    public void Does_not_degrade_for_a_component_that_has_no_variations()
    {
        var product = Product(isComponent: true);
        product.Variations.Should().BeEmpty("a meat option is a bare product — this is the normal case");

        var act = () => BasketComponentGuard.EnsureNotOrderedAlone(product);

        act.Should().Throw<BadRequestException>();
    }
}
