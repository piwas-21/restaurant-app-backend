using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Commands.ApproveOrderCommand;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class ApproveOrderCommandContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"preparationMinutes\":null}")]
    public void Missing_preparation_override_remains_distinct_from_zero(string json)
    {
        var command = JsonSerializer.Deserialize<ApproveOrderCommand>(json, WebJson);

        command.Should().NotBeNull();
        command!.PreparationMinutes.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"preparationMinutes\":0}", 0)]
    [InlineData("{\"preparationMinutes\":17}", 17)]
    public void Numeric_preparation_override_is_preserved(string json, int expected)
    {
        var command = JsonSerializer.Deserialize<ApproveOrderCommand>(json, WebJson);

        command.Should().NotBeNull();
        command!.PreparationMinutes.Should().Be(expected);
    }
}
