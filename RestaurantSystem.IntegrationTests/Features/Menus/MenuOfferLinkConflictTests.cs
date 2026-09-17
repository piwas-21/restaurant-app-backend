using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Menus;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

public sealed class MenuOfferLinkConflictTests
{
    [Fact]
    public void Offer_link_unique_violation_maps_to_the_duplicate_bad_request()
    {
        var postgres = new PostgresException(
            "duplicate", "23505", "unique_violation", "23505", constraintName: "ux_menu_definitions_parent_offer_product_id");

        var action = () => MenuOfferLinkConflict.ThrowIfExpected(
            new DbUpdateException("save failed", postgres));

        action.Should().Throw<BadRequestException>()
            .WithMessage(MenuOfferLinkConflict.DuplicateMessage);
    }

    [Fact]
    public void Serializable_abort_maps_to_a_reloadable_conflict_without_claiming_duplicate_state()
    {
        var postgres = new PostgresException("could not serialize access", "40001", "serialization_failure", "40001");

        var action = () => MenuOfferLinkConflict.ThrowIfExpected(postgres);

        action.Should().Throw<ConflictException>()
            .WithMessage(MenuOfferLinkConflict.ConcurrentMessage);
    }

    [Fact]
    public void Unrelated_database_errors_are_not_mapped_as_offer_link_conflicts()
    {
        var postgres = new PostgresException("other failure", "23505", "unique_violation", "23505", constraintName: "other_constraint");

        var action = () => MenuOfferLinkConflict.ThrowIfExpected(
            new DbUpdateException("save failed", postgres));

        action.Should().NotThrow();
    }
}
