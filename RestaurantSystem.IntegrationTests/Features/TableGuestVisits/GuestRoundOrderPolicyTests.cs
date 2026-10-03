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
    public void Readiness_requires_a_verified_visit_for_direct_dine_in_but_leaves_other_types_alone()
    {
        var directDineIn = new CreateOrderCommand { Type = OrderType.DineIn };

        var readinessFailure = GuestRoundOrderPolicy.ValidateSubmission(
            directDineIn, tableVisitReadinessEnabled: true);
        readinessFailure.Should().NotBeNull();
        readinessFailure!.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionRequired);
        GuestRoundOrderPolicy.ValidateSubmission(directDineIn, tableVisitReadinessEnabled: false)
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
