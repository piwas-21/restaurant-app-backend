using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Queries.GetBasketQuery;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public sealed class CustomerBasketReadLoggingTests
{
    [Fact]
    public async Task Successful_reads_keep_diagnostics_without_exposing_the_session_capability()
    {
        var session = Guid.NewGuid().ToString("D");
        var baskets = new Mock<IBasketService>();
        baskets.Setup(service => service.GetBasketAsync(session, null)).ReturnsAsync((BasketDto?)null);
        var user = new Mock<ICurrentUserService>();
        var logger = new Mock<ILogger<GetBasketQueryHandler>>();
        var handler = new GetBasketQueryHandler(baskets.Object, user.Object, logger.Object);

        var response = await handler.Handle(new GetBasketQuery(session), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Data!.SessionId.Should().Be(session);
        var entry = logger.Invocations.Should().ContainSingle(invocation => invocation.Method.Name == "Log").Subject;
        entry.Arguments[0].Should().Be(LogLevel.Information);
        entry.Arguments[2].ToString().Should().NotBeNullOrWhiteSpace().And.NotContain(session);
        var fields = entry.Arguments[2].Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>().Subject;
        fields.Should().NotContain(field => string.Equals(field.Value as string, session, StringComparison.Ordinal));
    }
}
