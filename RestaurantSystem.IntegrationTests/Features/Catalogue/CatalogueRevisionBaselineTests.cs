using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueRevisionBaselineTests
{
    [Fact]
    public void Baseline_serialization_keeps_the_existing_fields_property_name()
    {
        var json = CatalogueRevisionBaseline.Create(
            Revision(1, "source name", "source description", "translated name"), "item");
        using var document = JsonDocument.Parse(json);

        document.RootElement.TryGetProperty("fields", out var fields).Should().BeTrue();
        fields.ValueKind.Should().Be(JsonValueKind.Object);
        document.RootElement.TryGetProperty("fieldValues", out _).Should().BeFalse();
    }

    [Fact]
    public void Advancing_one_selected_field_keeps_every_unselected_field_on_its_prior_baseline()
    {
        var initial = Revision(1, "source name 1", "source description 1", "translated name 1");
        var current = Revision(2, "source name 2", "source description 2", "translated name 2");
        var initialBaseline = CatalogueRevisionBaseline.Create(initial, "item");

        var nextJson = CatalogueRevisionBaseline.Advance(
            initialBaseline,
            current,
            "item",
            ["name"],
            initial.Revision,
            initial.ContentHash);

        var next = CatalogueRevisionBaseline.Read(nextJson, "item", current.Revision, current.ContentHash);
        next["name"].Value.Should().Be("source name 2");
        next["description"].Value.Should().Be("source description 1");
        next["translations[tr].name"].Value.Should().Be("translated name 1");
        next["name"].SourceRevision.Should().Be(2);
        next["description"].SourceRevision.Should().Be(1);
    }

    [Fact]
    public void Local_hash_is_independent_of_dictionary_iteration_order()
    {
        var first = new Dictionary<string, string?>
        {
            ["name"] = "tenant name",
            ["translations[tr].name"] = "yerel ad"
        };
        var second = first.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        CatalogueRevisionBaseline.ComputeLocalHash(first)
            .Should().Be(CatalogueRevisionBaseline.ComputeLocalHash(second));
    }

    [Fact]
    public async Task Batch_loader_uses_one_central_request_and_maps_available_withdrawn_and_unknown()
    {
        var revision = Revision(2, "source name 2", "source description 2", "translated name 2");
        var body = JsonSerializer.SerializeToElement(new
        {
            items = new object[]
            {
                new { templateId = "revision-item", status = "available", revision,
                    adoptedRevisionWithdrawn = false },
                new { templateId = "withdrawn-item", status = "withdrawn", revision = (object?)null,
                    adoptedRevisionWithdrawn = true },
                new { templateId = "unknown-item", status = "notFound", revision = (object?)null,
                    adoptedRevisionWithdrawn = (bool?)null }
            }
        });
        var client = new Mock<ICentralCatalogueClient>(MockBehavior.Strict);
        client.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.Is<IReadOnlyList<CatalogueCurrentRevisionRequest>>(items => items.Count == 3),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogueProxyResponse(StatusCodes.Status200OK, body));

        var results = await new CataloguePublishedRevisionLoader(client.Object).LoadBatchAsync(
            [new("revision-item", 1), new("withdrawn-item", 4), new("unknown-item", 3)],
            CancellationToken.None);

        results["revision-item"].Status.Should().Be("Available");
        results["revision-item"].Revision!.Revision.Should().Be(2);
        results["withdrawn-item"].Status.Should().Be("AdoptedRevisionWithdrawn");
        results["unknown-item"].Status.Should().Be("Unknown");
        client.Verify(value => value.GetCurrentRevisionBatchAsync(It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(value => value.GetRevisionAsync(It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Batch_loader_marks_all_items_unavailable_when_response_is_incomplete()
    {
        var body = JsonSerializer.SerializeToElement(new
        {
            items = new[]
            {
                new { templateId = "revision-item", status = "notFound", revision = (object?)null,
                    adoptedRevisionWithdrawn = (bool?)null }
            }
        });
        var client = new Mock<ICentralCatalogueClient>();
        client.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogueProxyResponse(StatusCodes.Status200OK, body));

        var results = await new CataloguePublishedRevisionLoader(client.Object).LoadBatchAsync(
            [new("revision-item", 1), new("second-item", 1)], CancellationToken.None);

        results.Values.Should().OnlyContain(result => result.Status == "Unavailable");
    }

    [Fact]
    public async Task Batch_loader_splits_128_revisions_into_sixteen_bounded_requests()
    {
        var batchSizes = new List<int>();
        var webOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = new Mock<ICentralCatalogueClient>(MockBehavior.Strict);
        client.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CatalogueCurrentRevisionRequest> batch, CancellationToken _) =>
            {
                batchSizes.Add(batch.Count);
                var items = batch.Select(item => new
                {
                    item.TemplateId,
                    status = "available",
                    revision = Revision(2, "source name", "source description", "translated name") with
                    {
                        TemplateId = item.TemplateId
                    },
                    adoptedRevisionWithdrawn = false
                }).ToArray();
                var body = JsonSerializer.SerializeToElement(new { items }, webOptions);
                return Task.FromResult(new CatalogueProxyResponse(StatusCodes.Status200OK, body));
            });
        var requests = Enumerable.Range(0, CatalogueCurrentRevisionBatchLimits.MaximumItems)
            .Select(index => new CatalogueCurrentRevisionRequest($"template-{index}", 1))
            .ToArray();

        var results = await new CataloguePublishedRevisionLoader(client.Object)
            .LoadBatchAsync(requests, CancellationToken.None);

        results.Should().HaveCount(CatalogueCurrentRevisionBatchLimits.MaximumItems);
        results.Values.Should().OnlyContain(result => result.Status == "Available");
        batchSizes.Should().HaveCount(16);
        batchSizes.Should().OnlyContain(size => size == CatalogueCurrentRevisionBatchLimits.MaximumTenantBatchItems);
        client.Verify(value => value.GetRevisionAsync(It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CentralCatalogueTemplateRevision Revision(
        int revision,
        string name,
        string description,
        string translatedName) => new()
        {
            SchemaVersion = 1,
            TemplateId = "revision-item",
            Revision = revision,
            Type = "item",
            Name = name,
            Description = description,
            SourceLocale = "en",
            Translations = new Dictionary<string, CentralCatalogueTranslation>(StringComparer.Ordinal)
            {
                ["tr"] = new() { Name = translatedName, Description = $"translated description {revision}" }
            },
            Provenance = JsonSerializer.SerializeToElement(new { }),
            QualityStatus = "reviewed",
            CompatibleTenantContractVersions = [1],
            Payload = JsonSerializer.SerializeToElement(new { }),
            ContentHash = new string(revision == 1 ? 'a' : 'b', 64)
        };
}
