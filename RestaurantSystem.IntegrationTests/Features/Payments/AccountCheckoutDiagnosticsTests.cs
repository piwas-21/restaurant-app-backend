using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountCheckoutDiagnosticsTests
{
    [Fact]
    public void Provider_message_inner_exception_and_data_never_reach_the_logging_sink()
    {
        var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var failure = new HttpRequestException($"Provider request contained {secret}",
            new InvalidOperationException($"Sensitive context: {secret}"));
        failure.Data["providerPayload"] = secret;
        var logger = new Mock<ILogger>();
        var attemptId = Guid.NewGuid();

        AccountCheckoutDiagnostics.Warn(logger.Object, failure, AccountCheckoutFailurePhase.ProviderRecovery, attemptId);

        var entry = logger.Invocations.Single(invocation => invocation.Method.Name == nameof(ILogger.Log));
        var diagnostic = entry.Arguments[3].Should().BeAssignableTo<Exception>().Subject;
        diagnostic.InnerException.Should().BeNull();
        diagnostic.Data.Count.Should().Be(0);
        diagnostic.ToString().Should().Contain(nameof(HttpRequestException)).And.NotContain(secret);
        entry.Arguments[2].ToString().Should().Contain(attemptId.ToString())
            .And.Contain(nameof(HttpRequestException)).And.NotContain(secret);
    }
}
