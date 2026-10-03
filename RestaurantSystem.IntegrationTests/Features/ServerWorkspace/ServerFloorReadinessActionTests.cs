using FluentAssertions;
using RestaurantSystem.Api.Features.ServerWorkspace.Services;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.ServerWorkspace;

public sealed class ServerFloorReadinessActionTests
{
    [Fact]
    public void Needs_reset_table_offers_ready_action_only_when_rollout_is_enabled()
    {
        var table = NewTable(TableReadinessState.NeedsReset);

        Project(table, readinessEnabled: true).Should().ContainSingle().Which.Should().Be("MarkTableReady");
        Project(table, readinessEnabled: false).Should().ContainSingle().Which.Should().Be("StartTable");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Legacy_activity_routes_to_review_without_offering_readiness_reset(
        bool hasLegacyAmbiguity, bool hasLegacyOrders)
    {
        var actions = Project(
            NewTable(TableReadinessState.NeedsReset),
            readinessEnabled: true,
            hasLegacyAmbiguity,
            hasLegacyOrders);

        actions.Should().ContainSingle().Which.Should().Be("ReviewLegacy");
    }

    [Fact]
    public void Open_visit_and_inactive_table_never_offer_mark_ready()
    {
        var table = NewTable(TableReadinessState.NeedsReset);
        var session = new ServerFloorSessionSummaryDto { ServiceSessionId = Guid.NewGuid() };

        Project(table, readinessEnabled: true, session: session)
            .Should().NotContain("MarkTableReady");

        table.IsActive = false;
        Project(table, readinessEnabled: true).Should().BeEmpty();
    }

    private static List<string> Project(
        Table table,
        bool readinessEnabled,
        bool hasLegacyAmbiguity = false,
        bool hasLegacyOrders = false,
        ServerFloorSessionSummaryDto? session = null) =>
        ServerFloorActionProjection.Project(
            table,
            session,
            hasLegacyAmbiguity,
            hasLegacyOrders,
            readyCount: 0,
            hasCurrentReservation: false,
            currentRole: UserRole.Server,
            tableVisitReadinessEnabled: readinessEnabled);

    private static Table NewTable(TableReadinessState state) => new()
    {
        TableNumber = "QA-1",
        MaxGuests = 4,
        ReadinessState = state,
        CreatedBy = nameof(ServerFloorReadinessActionTests)
    };
}
