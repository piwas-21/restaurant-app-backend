using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public sealed class GuestRoundOrderPolicyTests
{
    [Fact]
    public void Readiness_allows_tableless_dine_in_but_requires_a_verified_visit_for_table_bound_dine_in()
    {
        var tablelessDineIn = new CreateOrderCommand { Type = OrderType.DineIn };
        var tableBoundDineIn = new CreateOrderCommand { Type = OrderType.DineIn, TableNumber = 4 };

        GuestRoundOrderPolicy.ValidateSubmission(tablelessDineIn, tableVisitReadinessEnabled: true)
            .Should().BeNull();
        GuestRoundOrderPolicy.ValidateSubmission(tablelessDineIn, tableVisitReadinessEnabled: false)
            .Should().BeNull();

        var readinessFailure = GuestRoundOrderPolicy.ValidateSubmission(
            tableBoundDineIn, tableVisitReadinessEnabled: true);
        readinessFailure.Should().NotBeNull();
        readinessFailure!.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionRequired);
        GuestRoundOrderPolicy.ValidateSubmission(tableBoundDineIn, tableVisitReadinessEnabled: false)
            .Should().BeNull();
        GuestRoundOrderPolicy.ValidateSubmission(
            new CreateOrderCommand { Type = OrderType.Takeaway }, tableVisitReadinessEnabled: true)
            .Should().BeNull();
    }

    [Fact]
    public void Verified_guest_round_context_remains_the_explicit_dine_in_path()
    {
        var command = new CreateOrderCommand
        {
            Type = OrderType.DineIn,
            ItemsAreServerPriced = true,
            GuestRoundContext = new TableGuestRoundContext(
                Guid.NewGuid(), Guid.NewGuid(), 4, "participant-hash", "basket-hash", "fingerprint"),
        };

        GuestRoundOrderPolicy.ValidateSubmission(command, tableVisitReadinessEnabled: true)
            .Should().BeNull();
    }
}
