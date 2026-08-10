using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.BackgroundServices;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// The loop around S7's two sweeps. What is worth pinning here is the safety default, not the
/// sweeping — that lives in the sweep tests.
/// </summary>
/// <remarks>
/// This service is CLAUDE.md §9 data-loss class: its expiry sweep cancels orders. The claim that it
/// "deploys inert to the whole fleet" rests entirely on <c>Enabled</c> defaulting to false, and a
/// default nothing asserts is a default one careless edit removes — on a service that would then
/// start cancelling orders on every tenant at once.
/// </remarks>
public class CheckoutReconciliationServiceTests
{
    [Fact]
    public void The_capability_is_off_unless_configuration_turns_it_on()
    {
        new CheckoutReconciliationSettings().Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Disabled means it never even resolves a scope. The provider throws if touched, so this fails
    /// if the guard is moved below any service resolution rather than being the first thing done.
    /// </summary>
    [Fact]
    public async Task Disabled_it_never_touches_the_service_provider()
    {
        var provider = new Mock<IServiceProvider>(MockBehavior.Strict);

        var service = new CheckoutReconciliationService(
            provider.Object,
            NullLogger<CheckoutReconciliationService>.Instance,
            Options.Create(new CheckoutReconciliationSettings { Enabled = false }));

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        provider.VerifyNoOtherCalls();
    }
}
