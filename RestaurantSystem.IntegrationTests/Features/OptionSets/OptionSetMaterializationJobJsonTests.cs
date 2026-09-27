using FluentAssertions;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.OptionSets;

public sealed class OptionSetMaterializationJobJsonTests
{
    [Fact]
    public void Fingerprint_is_independent_of_object_and_dictionary_property_order()
    {
        var first = CreateRequest(reverseOverrides: false);
        var reordered = CreateRequest(reverseOverrides: true);

        OptionSetMaterializationJobJson.Fingerprint(first)
            .Should().Be(OptionSetMaterializationJobJson.Fingerprint(reordered));
    }

    [Fact]
    public void Fingerprint_preserves_target_sequence_because_menu_versions_advance_in_order()
    {
        var first = CreateRequest(reverseOverrides: false);
        var reversed = CreateRequest(reverseOverrides: false);
        reversed.Targets = reversed.Targets.Reverse().ToArray();

        OptionSetMaterializationJobJson.Fingerprint(first)
            .Should().NotBe(OptionSetMaterializationJobJson.Fingerprint(reversed));
    }

    [Fact]
    public void Snapshot_round_trip_keeps_wire_enum_values_and_target_data()
    {
        var request = CreateRequest(reverseOverrides: false);
        var snapshot = OptionSetMaterializationJobJson.Serialize(request);
        var restored = OptionSetMaterializationJobJson.Deserialize<OptionSetMaterializationRequest>(snapshot);

        snapshot.Should().Contain("bundleChoice");
        restored.Targets.Select(target => target.Role).Should()
            .OnlyContain(role => role == OptionSetAttachmentRole.BundleChoice);
        restored.Targets.Select(target => target.TargetKey).Should().Equal("menu-0", "menu-1");
    }

    private static OptionSetMaterializationRequest CreateRequest(bool reverseOverrides)
    {
        var entryIds = new[] { Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("20000000-0000-0000-0000-000000000002") };
        var overrides = new Dictionary<Guid, OptionSetEntryOverride>();
        var sequence = reverseOverrides ? entryIds.Reverse() : entryIds;
        foreach (var id in sequence)
        {
            overrides[id] = new OptionSetEntryOverride { AdditionalPrice = id == entryIds[0] ? 1.25m : 2m };
        }

        return new OptionSetMaterializationRequest
        {
            ExpectedSetVersion = 4,
            IdempotencyKey = "large-fanout-1",
            Targets = [
                new OptionSetMaterializationTargetRequest
                {
                    TargetKey = "menu-0",
                    Role = OptionSetAttachmentRole.BundleChoice,
                    TargetProductId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
                    TargetMenuSectionId = Guid.Parse("40000000-0000-0000-0000-000000000004"),
                    ExpectedMenuAuthoringVersion = 2,
                    Overrides = overrides
                },
                new OptionSetMaterializationTargetRequest
                {
                    TargetKey = "menu-1",
                    Role = OptionSetAttachmentRole.BundleChoice,
                    TargetProductId = Guid.Parse("50000000-0000-0000-0000-000000000005"),
                    TargetMenuSectionId = Guid.Parse("60000000-0000-0000-0000-000000000006"),
                    ExpectedMenuAuthoringVersion = 1
                }
            ]
        };
    }
}
