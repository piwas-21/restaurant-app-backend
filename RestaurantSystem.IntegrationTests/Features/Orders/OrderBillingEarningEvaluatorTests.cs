using System.Globalization;
using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderBillingEarningEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_reads_once_and_copies_the_deterministic_winner()
    {
        var priorityTwo = Rule("93000000-0000-0000-0000-000000000003", 0m, 80, 2);
        var higherMinimum = Rule("93000000-0000-0000-0000-000000000002", 20m, 30, 1);
        var stableIdTieWinner = Rule("93000000-0000-0000-0000-000000000001", 10m, 7, 1);
        var stableIdTieLoser = Rule("93000000-0000-0000-0000-000000000004", 10m, 8, 1);
        var rules = new[] { priorityTwo, higherMinimum, stableIdTieLoser, stableIdTieWinner };
        var (service, evaluator) = Create(rules);

        var result = await evaluator.EvaluateAsync(50m);

        result.CandidatePoints.Should().Be(7);
        result.AlgorithmVersion.Should().Be(OrderBillingEarningEvaluator.AlgorithmVersion);
        result.MatchedRule.Should().BeEquivalentTo(new OrderBillingEarningRuleEvidence(
            stableIdTieWinner.Id, stableIdTieWinner.Name, stableIdTieWinner.MinOrderAmount,
            stableIdTieWinner.MaxOrderAmount, stableIdTieWinner.PointsAwarded, stableIdTieWinner.Priority));
        service.Verify(item => item.GetActiveRulesAsync(It.IsAny<CancellationToken>()), Times.Once);
        service.VerifyNoOtherCalls();

        var (shuffledService, shuffledEvaluator) = Create(rules.Reverse().ToArray());
        var shuffled = await shuffledEvaluator.EvaluateAsync(50m);
        shuffled.Should().BeEquivalentTo(result);
        shuffledService.Verify(item => item.GetActiveRulesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_uses_lower_priority_number_before_minimum()
    {
        var higherPriorityNumber = Rule("93000000-0000-0000-0000-000000000001", 0m, 19, 2);
        var lowerPriorityNumber = Rule("93000000-0000-0000-0000-000000000002", 40m, 4, 1);
        var (_, evaluator) = Create(higherPriorityNumber, lowerPriorityNumber);

        var result = await evaluator.EvaluateAsync(50m);

        result.CandidatePoints.Should().Be(4);
        result.MatchedRule!.Id.Should().Be(lowerPriorityNumber.Id);
    }

    [Fact]
    public async Task EvaluateAsync_returns_evaluated_zero_when_no_rule_matches()
    {
        var nonmatching = Rule("93000000-0000-0000-0000-000000000001", 100m, 12, 0);
        var (_, evaluator) = Create(nonmatching);

        var result = await evaluator.EvaluateAsync(50m);

        result.CandidatePoints.Should().Be(0);
        result.AlgorithmVersion.Should().Be(OrderBillingEarningEvaluator.AlgorithmVersion);
        result.RuleSetFingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
        result.MatchedRule.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_preserves_a_matched_zero_point_rule()
    {
        var zeroPointRule = Rule("93000000-0000-0000-0000-000000000001", 50m, 0, 0, maximum: 50m);
        var (_, evaluator) = Create(zeroPointRule);

        var result = await evaluator.EvaluateAsync(50m);

        result.CandidatePoints.Should().Be(0);
        result.MatchedRule.Should().NotBeNull();
        result.MatchedRule!.Id.Should().Be(zeroPointRule.Id);
    }

    [Fact]
    public async Task Fingerprint_uses_the_fixed_canonical_serialization()
    {
        var rule = Rule("93000000-0000-0000-0000-000000000001", 0m, 1, 0);
        var (_, evaluator) = Create(rule);

        var result = await evaluator.EvaluateAsync(50m);

        result.RuleSetFingerprint.Should().Be("3e9f4fd1811ee5264e5810bf9ccdbc15451e7aafddccf19ce4e2ef725689929d"); // pragma: allowlist secret
    }

    [Fact]
    public async Task Fingerprint_is_order_independent_and_covers_nonmatching_active_facts_only()
    {
        var matching = Rule("93000000-0000-0000-0000-000000000001", 0m, 3, 0);
        var nonmatching = Rule("93000000-0000-0000-0000-000000000002", 100m, 5, 1);
        var inactive = Rule("93000000-0000-0000-0000-000000000003", 200m, 6, 2, active: false);
        var (_, firstEvaluator) = Create(matching, nonmatching, inactive);
        var first = await firstEvaluator.EvaluateAsync(50m);

        var (_, changedOrderEvaluator) = Create(inactive, nonmatching, matching);
        var changedOrder = await changedOrderEvaluator.EvaluateAsync(50m);
        changedOrder.RuleSetFingerprint.Should().Be(first.RuleSetFingerprint);

        inactive.Name = "renamed inactive row";
        var (_, changedInactiveEvaluator) = Create(matching, inactive, nonmatching);
        var changedInactive = await changedInactiveEvaluator.EvaluateAsync(50m);
        changedInactive.RuleSetFingerprint.Should().Be(first.RuleSetFingerprint);

        nonmatching.PointsAwarded++;
        nonmatching.Priority++;
        var (_, changedActiveEvaluator) = Create(matching, nonmatching, inactive);
        var changedActive = await changedActiveEvaluator.EvaluateAsync(50m);
        changedActive.RuleSetFingerprint.Should().NotBe(first.RuleSetFingerprint);
    }

    [Theory]
    [InlineData("-1", null, 1)]
    [InlineData("0", "-1", 1)]
    [InlineData("2", "1", 1)]
    [InlineData("0", null, -1)]
    public async Task EvaluateAsync_rejects_corrupt_amounts_and_negative_points(
        string minimum,
        string? maximum,
        int points)
    {
        var minimumAmount = decimal.Parse(minimum, CultureInfo.InvariantCulture);
        decimal? maximumAmount = maximum is null ? null : decimal.Parse(maximum, CultureInfo.InvariantCulture);
        var invalid = Rule("93000000-0000-0000-0000-000000000001", minimumAmount,
            points, 0, maximumAmount);
        var (_, evaluator) = Create(invalid);

        var act = () => evaluator.EvaluateAsync(50m);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task EvaluateAsync_rejects_duplicate_active_ids()
    {
        var id = Guid.Parse("93000000-0000-0000-0000-000000000001");
        var first = Rule(id, 0m, 1, 0);
        var duplicate = Rule(id, 10m, 2, 1);
        var (_, evaluator) = Create(first, duplicate);

        var act = () => evaluator.EvaluateAsync(50m);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task EvaluateAsync_rejects_empty_identity_and_name_on_active_rows()
    {
        var emptyId = Rule(Guid.Empty, 0m, 1, 0);
        var (_, emptyIdEvaluator) = Create(emptyId);
        var emptyIdAct = () => emptyIdEvaluator.EvaluateAsync(50m);
        await emptyIdAct.Should().ThrowAsync<ConflictException>();

        var emptyName = Rule(Guid.NewGuid(), 0m, 1, 0, name: "  ");
        var (_, emptyNameEvaluator) = Create(emptyName);
        var emptyNameAct = () => emptyNameEvaluator.EvaluateAsync(50m);
        await emptyNameAct.Should().ThrowAsync<ConflictException>();
    }

    private static (Mock<IPointEarningRuleService> Service, OrderBillingEarningEvaluator Evaluator) Create(
        params PointEarningRule[] rules)
    {
        var service = new Mock<IPointEarningRuleService>(MockBehavior.Strict);
        service.Setup(item => item.GetActiveRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules.ToList());
        return (service, new OrderBillingEarningEvaluator(service.Object));
    }

    private static PointEarningRule Rule(
        string id,
        decimal minimum,
        int points,
        int priority,
        decimal? maximum = null,
        bool active = true,
        string name = "earning rule") => Rule(Guid.Parse(id), minimum, points, priority, maximum, active, name);

    private static PointEarningRule Rule(
        Guid id,
        decimal minimum,
        int points,
        int priority,
        decimal? maximum = null,
        bool active = true,
        string name = "earning rule") => new()
        {
            Id = id,
            Name = name,
            MinOrderAmount = minimum,
            MaxOrderAmount = maximum,
            PointsAwarded = points,
            Priority = priority,
            IsActive = active,
            CreatedBy = nameof(OrderBillingEarningEvaluatorTests)
        };
}
