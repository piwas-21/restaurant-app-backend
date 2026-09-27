using FluentAssertions;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueImportIdempotencyRulesTests
{
    [Fact]
    public void Partial_attempt_replays_only_for_the_same_key_and_original_version()
    {
        var session = Session(CatalogueImportStatus.PartiallyImported, version: 8, key: "first", expectedVersion: 2);

        CatalogueImportIdempotencyRules.CanReplayResult(session, "first", 2).Should().BeTrue();
        CatalogueImportIdempotencyRules.CanReplayResult(session, "second", 2).Should().BeFalse();
        CatalogueImportIdempotencyRules.CanReplayResult(session, "first", 8).Should().BeFalse();
    }

    [Fact]
    public void Partial_attempt_accepts_a_fresh_attempt_only_at_the_current_session_version()
    {
        var session = Session(CatalogueImportStatus.Failed, version: 8, key: "first", expectedVersion: 2);

        CatalogueImportIdempotencyRules.CanStartAttempt(session, "second", 8).Should().BeTrue();
        CatalogueImportIdempotencyRules.CanStartAttempt(session, "first", 7).Should().BeFalse();
    }

    [Fact]
    public void Interrupted_attempt_resumes_only_with_its_original_key_and_version()
    {
        var session = Session(CatalogueImportStatus.Importing, version: 8, key: "first", expectedVersion: 2);

        CatalogueImportIdempotencyRules.CanStartAttempt(session, "first", 2).Should().BeTrue();
        CatalogueImportIdempotencyRules.CanStartAttempt(session, "second", 2).Should().BeFalse();
        CatalogueImportIdempotencyRules.CanStartAttempt(session, "first", 8).Should().BeFalse();
    }

    private static CatalogueImportSession Session(
        CatalogueImportStatus status,
        int version,
        string key,
        int expectedVersion) => new()
        {
            Status = status,
            Version = version,
            LastImportIdempotencyKey = key,
            LastImportExpectedVersion = expectedVersion,
            LastImportResultJson = "{}",
            CreatedBy = "test"
        };
}
