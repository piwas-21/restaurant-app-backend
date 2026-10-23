using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public sealed class CustomerTablePlanSettingsValidationTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Native_limit_configuration_preserves_the_versioned_client_contract(int shares, bool valid)
    {
        var settings = new AccountPaymentSettings { MaximumNativePlanShares = shares };
        Validator.TryValidateObject(settings, new ValidationContext(settings), [], validateAllProperties: true)
            .Should().Be(valid);
    }
}
