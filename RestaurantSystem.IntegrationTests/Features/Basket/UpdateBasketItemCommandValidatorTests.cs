using FluentAssertions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Basket.Commands.UpdateBasketItemCommand;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public sealed class UpdateBasketItemCommandValidatorTests
{
    [Fact]
    public void Validate_UsesConfiguredBasketQuantityLimit()
    {
        var validator = new UpdateBasketItemCommandValidator(Options.Create(new BasketSettings
        {
            MaxQuantityPerItem = 3
        }));
        var command = new UpdateBasketItemCommand("session", Guid.NewGuid(), 4, null);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.PropertyName == "Quantity"
            && error.ErrorMessage == "Quantity cannot exceed 3");
    }
}
