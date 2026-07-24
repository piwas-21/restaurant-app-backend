using FluentAssertions;
using RestaurantSystem.Api.Features.Reservations;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Reservations;

/// <summary>
/// Pure (DB-free) unit tests for the POST /api/tables geometry coercion
/// (FLOOR-PLAN-REVAMP §5.2/§6). Pins the era-straddling branch logic that the
/// endpoint relies on: honour plausibly-metric input, discard pixel-scale
/// input for a seats-derived footprint, recentre out-of-bounds positions, and
/// normalise the legacy "circle" shape.
/// </summary>
public class TableGeometryDefaultsTests
{
    private const decimal PlanWidth = 12m;
    private const decimal PlanHeight = 10m;

    [Fact]
    public void MetreFootprint_PlausiblyMetricSize_IsHonoured()
    {
        var (width, height) = TableGeometryDefaults.MetreFootprint(1.5m, 0.9m, 6, PlanWidth, PlanHeight);

        width.Should().Be(1.5m);
        height.Should().Be(0.9m);
    }

    [Theory]
    [InlineData(2, 0.70, 0.70)]
    [InlineData(4, 1.20, 0.80)]
    [InlineData(6, 1.80, 0.90)]
    [InlineData(8, 2.40, 1.00)]
    public void MetreFootprint_OmittedSize_DerivesFromSeats(int seats, decimal expectedW, decimal expectedH)
    {
        var (width, height) = TableGeometryDefaults.MetreFootprint(null, null, seats, PlanWidth, PlanHeight);

        width.Should().Be(expectedW);
        height.Should().Be(expectedH);
    }

    [Fact]
    public void MetreFootprint_LegacyPixelSize_IsDiscardedForSeatsDerived()
    {
        // The deployed frontend posts an 80×80 px circle for a 4-top: > the
        // metre plausibility ceiling, so it must fall back to the 4-seat size,
        // never be stored as an 80-metre table.
        var (width, height) = TableGeometryDefaults.MetreFootprint(80m, 80m, 4, PlanWidth, PlanHeight);

        width.Should().Be(1.20m);
        height.Should().Be(0.80m);
    }

    [Fact]
    public void MetreFootprint_MetricSizeLargerThanPlan_IsClampedIntoPlan()
    {
        var (width, height) = TableGeometryDefaults.MetreFootprint(9m, 8m, 4, 5m, 4m);

        width.Should().Be(5m);
        height.Should().Be(4m);
    }

    [Fact]
    public void MetrePosition_InBounds_IsHonoured()
    {
        var (x, y) = TableGeometryDefaults.MetrePosition(6m, 4m, PlanWidth, PlanHeight);

        x.Should().Be(6m);
        y.Should().Be(4m);
    }

    [Fact]
    public void MetrePosition_LegacyPixelCoordinates_RecentreOnThePlan()
    {
        // 260×210 px (the old canvas centre) is far outside the metre bounds.
        var (x, y) = TableGeometryDefaults.MetrePosition(260m, 210m, PlanWidth, PlanHeight);

        x.Should().Be(PlanWidth / 2m);
        y.Should().Be(PlanHeight / 2m);
    }

    [Fact]
    public void MetrePosition_Omitted_CentresOnThePlan()
    {
        var (x, y) = TableGeometryDefaults.MetrePosition(null, null, PlanWidth, PlanHeight);

        x.Should().Be(PlanWidth / 2m);
        y.Should().Be(PlanHeight / 2m);
    }

    [Theory]
    [InlineData("circle", "round")]
    [InlineData("CIRCLE", "round")]
    [InlineData("", "round")]
    [InlineData("  ", "round")]
    [InlineData(null, "round")]
    [InlineData("square", "square")]
    [InlineData("booth", "booth")]
    public void NormalizeShape_MapsLegacyAndBlankToRound(string? input, string expected)
    {
        TableGeometryDefaults.NormalizeShape(input).Should().Be(expected);
    }
}
